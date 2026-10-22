using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JokiAutomation;
using JokiAutomation.Tests.Fakes;
using NUnit.Framework;

namespace JokiAutomation.Tests.Regression
{
    /// <summary>
    /// Reproduces the production defect where the first "Predigt" request after process
    /// start reported CLI success while the queued audio profile command had not yet been
    /// sent to the Raspberry Pi, because QueueAudioProfileByNameAsync only waits for the
    /// job to be enqueued onto the shared, per-Form1-instance background pipeline
    /// (_audioPipelineTail), not for it to actually be sent/verified.
    ///
    /// See AudioPipelineLifetimeAnalysis.md for the full call-chain analysis.
    /// </summary>
    [TestFixture]
    [Category("Regression.Audio")]
    [Apartment(ApartmentState.STA)]
    public sealed class AudioPipelineLifetimeRegressionTests
    {
        private const string PredigtAudioCommandFragment = "/home/pi/JokiAutomation/RasPiAutomation 30 114";

        [Test]
        public async Task PowerPointLike_PredigtRequest_SurvivesFormAndCliCompletion()
        {
            var fakeExecutor = new FakeRasPiCommandExecutor
            {
                // Hardware/SSH has not yet responded - represents the in-flight window.
                AllowCompletionGate = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously)
            };

            using (Form1 form = CreateProductionForm1(fakeExecutor))
            {
                // Drive the real public CLI entry point, exactly as Program.
                // ExecuteDispatchedCommandAsync does for a dispatched "Predigt" command.
                Task<int> cliTask = form.CommandInterpreterAsync(new[] { "JokiAutomation.exe", "Predigt" });

                // --- Wait for the executor to actually be invoked (still gated/blocked) ---
                FakeRasPiCommandExecutor.Invocation invocationDuringExecution = await WaitForInvocationStartAsync(fakeExecutor, TimeSpan.FromSeconds(5));

                bool executorStarted = invocationDuringExecution != null;
                bool executorCompletedWhileGated = invocationDuringExecution?.Completed == true;
                bool cliTaskStillPendingWhileGated = !cliTask.IsCompleted;

                // Hardware eventually responds.
                fakeExecutor.AllowCompletionGate.TrySetResult(null);

                int exitCode = await cliTask;

                FakeRasPiCommandExecutor.Invocation finalInvocation;
                lock (fakeExecutor.Invocations)
                {
                    finalInvocation = fakeExecutor.Invocations.Count > 0 ? fakeExecutor.Invocations[0] : null;
                }

                TestContext.WriteLine($"CLI/Request ExitCode: {exitCode}");
                TestContext.WriteLine($"Executor gestartet (w\u00E4hrend Gate aktiv): {executorStarted}");
                TestContext.WriteLine($"Executor bereits abgeschlossen w\u00E4hrend Gate aktiv: {executorCompletedWhileGated}");
                TestContext.WriteLine($"CLI-Task (CommandInterpreterAsync) noch ausstehend w\u00E4hrend Gate aktiv: {cliTaskStillPendingWhileGated}");
                TestContext.WriteLine($"Executor abgeschlossen (final): {finalInvocation?.Completed}");
                TestContext.WriteLine($"Command=30/ID=114 aufgezeichnet: {finalInvocation?.Command}");
                TestContext.WriteLine($"Exception beobachtet: {finalInvocation?.Exception}");

                // Verify the fix: audio still dispatches/runs in parallel with the
                // visual/PTZ/ATEM work of the "Predigt" case (the executor is reached and
                // started well before the gate is released), but the CLI/CommandInterpreter
                // path now only reports success once that audio request has actually
                // completed.
                Assert.That(executorStarted, Is.True,
                    "Der Executor muss gestartet worden sein, w\u00E4hrend der CLI-Aufruf noch l\u00E4uft (Audio l\u00E4uft parallel zu PTZ/Video).");
                Assert.That(executorCompletedWhileGated, Is.False,
                    "Der Executor darf w\u00E4hrend des aktiven Gates noch NICHT abgeschlossen sein.");
                Assert.That(cliTaskStillPendingWhileGated, Is.True,
                    "CommandInterpreterAsync darf nicht zur\u00FCckkehren, solange der Audioauftrag noch nicht abgeschlossen ist (Fix-Verhalten).");
                Assert.That(exitCode, Is.EqualTo(CommandExitCodes.Success));
                Assert.That(finalInvocation, Is.Not.Null, "Command=30 / ID=114 wurde nicht aufgezeichnet.");
                Assert.That(finalInvocation.Command.Contains(PredigtAudioCommandFragment), Is.True,
                    $"Erwartetes Fragment '{PredigtAudioCommandFragment}' nicht in '{finalInvocation.Command}' gefunden.");
                Assert.That(finalInvocation.Exception, Is.Null, "Keine Exception erwartet in diesem Lauf.");
                Assert.That(finalInvocation.Completed, Is.True, "Executor muss final abgeschlossen sein.");
            }
        }

        private static async Task<FakeRasPiCommandExecutor.Invocation> WaitForInvocationStartAsync(
            FakeRasPiCommandExecutor fakeExecutor,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                FakeRasPiCommandExecutor.Invocation invocation;
                lock (fakeExecutor.Invocations)
                {
                    invocation = fakeExecutor.Invocations.Count > 0 ? fakeExecutor.Invocations[0] : null;
                }

                if (invocation != null && invocation.Started.IsCompleted)
                {
                    return invocation;
                }

                await Task.Delay(10);
            }

            return null;
        }

        private static Form1 CreateProductionForm1(FakeRasPiCommandExecutor fakeExecutor)
        {
            Form1 form = new Form1();

            FieldInfo audioMixField = typeof(Form1).GetField("_audioMix", BindingFlags.NonPublic | BindingFlags.Instance);
            var audioMix = (AudioMix)audioMixField.GetValue(form);

            var rasPi = new RasPi(fakeExecutor);
            rasPi.SetConnectionSettingsForTests("192.0.2.10", "pi", "test-password");
            rasPi.SetLogSinkForTests(_ => { });

            audioMix._rasPi = rasPi;

            return form;
        }
    }
}
