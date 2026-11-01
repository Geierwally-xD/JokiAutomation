using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JokiAutomation;
using NUnit.Framework;

namespace JokiAutomation.Tests.Unit
{
    // Phase 2C/2D: single, small, parametric ScenePlan-/Dispatcher test suite proving the migrated
    // scenes' ScenePlan shape and parallel Audio/Video execution contract, without touching any
    // hardware (ATEM/RasPi/PTZ) and without re-entering Form1.CommandInterpreterAsync.
    [TestFixture]
    [Category("Unit")]
    public sealed class PredigtScenePlanDispatcherTests
    {
        [TestCase("Predigt")]
        [TestCase("Gebet")]
        [TestCase("BandMulti")]
        [TestCase("LesungMulti")]
        [TestCase("Text")]
        public void CreateLegacyScenePlan_MigratedScene_HasExpectedShapeAndExecutionMode(string commandName)
        {
            // Phase 2D: exercises the real production ScenePlan factory (Program.CreateLegacyScenePlan)
            // via reflection, instead of duplicating its routing decision in a fake test-only planner.
            Type programType = typeof(CommandExitCodes).Assembly.GetType("JokiAutomation.Program");
            Assert.That(programType, Is.Not.Null, "Could not locate JokiAutomation.Program via reflection.");

            MethodInfo createLegacyScenePlan = programType.GetMethod(
                "CreateLegacyScenePlan",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(createLegacyScenePlan, Is.Not.Null, "Could not locate Program.CreateLegacyScenePlan via reflection.");

            Type requestType = typeof(CommandExitCodes).Assembly.GetType("JokiAutomation.SceneDispatchRequest");
            object request = Activator.CreateInstance(requestType);
            requestType.GetProperty("ExternalRequestId").SetValue(request, "ext-" + commandName.ToLowerInvariant());
            requestType.GetProperty("Source").SetValue(request, "CLI");
            requestType.GetProperty("CommandName").SetValue(request, commandName);
            requestType.GetProperty("Arguments").SetValue(request, new[] { commandName });

            object scenePlan = createLegacyScenePlan.Invoke(null, new object[] { null, request, CancellationToken.None });
            Type scenePlanType = scenePlan.GetType();

            int legacyCount = ((System.Collections.ICollection)scenePlanType.GetProperty("LegacyCommands").GetValue(scenePlan)).Count;
            int audioCount = ((System.Collections.ICollection)scenePlanType.GetProperty("AudioCommands").GetValue(scenePlan)).Count;
            int videoCount = ((System.Collections.ICollection)scenePlanType.GetProperty("VideoCommands").GetValue(scenePlan)).Count;
            int positionCount = ((System.Collections.ICollection)scenePlanType.GetProperty("PositionCommands").GetValue(scenePlan)).Count;
            int uiCount = ((System.Collections.ICollection)scenePlanType.GetProperty("UiCommands").GetValue(scenePlan)).Count;
            object executionMode = scenePlanType.GetProperty("ExecutionMode").GetValue(scenePlan);

            Assert.That(legacyCount, Is.EqualTo(0), $"{commandName}: LegacyCount must be 0.");
            Assert.That(audioCount, Is.EqualTo(1), $"{commandName}: AudioCount must be 1.");
            Assert.That(videoCount, Is.EqualTo(1), $"{commandName}: VideoCount must be 1.");
            Assert.That(positionCount, Is.EqualTo(0), $"{commandName}: PositionCount must be 0.");
            Assert.That(uiCount, Is.EqualTo(0), $"{commandName}: UiCount must be 0.");
            Assert.That(executionMode.ToString(), Is.EqualTo("ParallelAudioVideo"), $"{commandName}: ExecutionMode must be ParallelAudioVideo.");
        }

        [TestCase("Predigt")]
        [TestCase("Gebet")]
        [TestCase("BandMulti")]
        [TestCase("LesungMulti")]
        [TestCase("Text")]
        public async Task ScenePlan_RunsAudioAndVideoInParallel_ExactlyOnce_NoLegacy(string commandName)
        {
            int legacyCalls = 0;
            int audioCalls = 0;
            int videoCalls = 0;

            var audioStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var videoStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseAudio = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseVideo = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Guid expectedSceneId = Guid.NewGuid();
            Guid expectedRequestId = Guid.NewGuid();
            string expectedExternalRequestId = "ext-" + commandName.ToLowerInvariant() + "-123";
            const string expectedSource = "CLI";

            ScenePlan ScenePlanner(SceneDispatchRequest request, CancellationToken cancellationToken)
            {
                PipelineCommand MakeCommand(CommandPipeline pipeline) => new PipelineCommand
                {
                    RequestId = expectedRequestId,
                    SceneId = expectedSceneId,
                    Pipeline = pipeline,
                    CommandName = commandName,
                    Arguments = Array.Empty<string>(),
                    CreatedAt = DateTimeOffset.UtcNow,
                    CancellationToken = cancellationToken,
                    ExternalRequestId = expectedExternalRequestId,
                    ExecutorSource = expectedSource
                };

                return new ScenePlan
                {
                    SceneId = expectedSceneId,
                    SceneName = commandName,
                    ExecutionMode = SceneExecutionMode.ParallelAudioVideo,
                    LegacyCommands = Array.Empty<PipelineCommand>(),
                    AudioCommands = new[] { MakeCommand(CommandPipeline.Audio) },
                    VideoCommands = new[] { MakeCommand(CommandPipeline.Video) },
                    PositionCommands = Array.Empty<PipelineCommand>(),
                    UiCommands = Array.Empty<PipelineCommand>()
                };
            }

            async Task<int> LegacyExecutor(PipelineCommand command)
            {
                Interlocked.Increment(ref legacyCalls);
                return CommandExitCodes.Success;
            }

            async Task<int> AudioExecutor(PipelineCommand command)
            {
                Interlocked.Increment(ref audioCalls);
                audioStarted.TrySetResult(true);
                await releaseAudio.Task.ConfigureAwait(false);
                return CommandExitCodes.Success;
            }

            async Task<int> VideoExecutor(PipelineCommand command)
            {
                Interlocked.Increment(ref videoCalls);
                videoStarted.TrySetResult(true);
                await releaseVideo.Task.ConfigureAwait(false);
                return CommandExitCodes.Success;
            }

            async Task<int> NotExpectedExecutor(PipelineCommand command)
            {
                throw new InvalidOperationException($"Position/Ui executor must not be invoked for {commandName}.");
            }

            var dispatcher = new CommandDispatcher(
                ScenePlanner,
                LegacyExecutor,
                AudioExecutor,
                VideoExecutor,
                NotExpectedExecutor,
                NotExpectedExecutor);

            try
            {
                Task<SceneDispatchResult> dispatchTask = dispatcher.DispatchAsync(
                    new SceneDispatchRequest
                    {
                        ExternalRequestId = expectedExternalRequestId,
                        Source = expectedSource,
                        CommandName = commandName,
                        Arguments = Array.Empty<string>(),
                        WaitMode = CommandWaitMode.WaitForScene,
                        CreatedAt = DateTimeOffset.UtcNow
                    },
                    CancellationToken.None);

                // Audio must start before Video completes, and Video must start before Audio completes:
                // both executors have to reach their "started" signal while the other is still blocked.
                await Task.WhenAll(audioStarted.Task, videoStarted.Task).ConfigureAwait(false);

                // Scene must remain pending while either side is still open.
                await Task.Delay(50);
                Assert.That(dispatchTask.IsCompleted, Is.False, $"{commandName}: Scene completed before both Audio and Video finished.");

                releaseAudio.TrySetResult(true);
                releaseVideo.TrySetResult(true);

                SceneDispatchResult result = await dispatchTask.ConfigureAwait(false);

                Assert.That(result.State, Is.EqualTo(SceneExecutionState.Completed));
                Assert.That(result.ExitCode, Is.EqualTo(CommandExitCodes.Success));
                Assert.That(result.SceneId, Is.EqualTo(expectedSceneId));
                Assert.That(result.ExternalRequestId, Is.EqualTo(expectedExternalRequestId));

                Assert.That(legacyCalls, Is.EqualTo(0), $"{commandName} must not execute through the legacy path.");
                Assert.That(audioCalls, Is.EqualTo(1), $"{commandName} audio must execute exactly once.");
                Assert.That(videoCalls, Is.EqualTo(1), $"{commandName} video must execute exactly once.");
            }
            finally
            {
                await dispatcher.StopAsync().ConfigureAwait(false);
            }
        }
    }
}
