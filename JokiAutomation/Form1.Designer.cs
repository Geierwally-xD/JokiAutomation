using System.Windows.Forms;

namespace JokiAutomation
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.TabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.button15 = new System.Windows.Forms.Button();
            this.button1 = new System.Windows.Forms.Button();
            this.label4 = new System.Windows.Forms.Label();
            this.dateTimePicker1 = new System.Windows.Forms.DateTimePicker();
            this.label3 = new System.Windows.Forms.Label();
            this.textBox2 = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.listBox1 = new System.Windows.Forms.ListBox();
            this.label1 = new System.Windows.Forms.Label();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.tabPage2 = new System.Windows.Forms.TabPage();
            this.button13 = new System.Windows.Forms.Button();
            this.button12 = new System.Windows.Forms.Button();
            this.button10 = new System.Windows.Forms.Button();
            this.listBox2 = new System.Windows.Forms.ListBox();
            this.button3 = new System.Windows.Forms.Button();
            this.button2 = new System.Windows.Forms.Button();
            this.label5 = new System.Windows.Forms.Label();
            this.richTextBox1 = new System.Windows.Forms.RichTextBox();
            this.tabPage3 = new System.Windows.Forms.TabPage();
            this.button11 = new System.Windows.Forms.Button();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.label9 = new System.Windows.Forms.Label();
            this.label8 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.button16 = new System.Windows.Forms.Button();
            this.trackBar4 = new System.Windows.Forms.TrackBar();
            this.trackBar3 = new System.Windows.Forms.TrackBar();
            this.trackBar2 = new System.Windows.Forms.TrackBar();
            this.trackBar1 = new System.Windows.Forms.TrackBar();
            this.button14 = new System.Windows.Forms.Button();
            this.button9 = new System.Windows.Forms.Button();
            this.listBox4 = new System.Windows.Forms.ListBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.button8 = new System.Windows.Forms.Button();
            this.button7 = new System.Windows.Forms.Button();
            this.button6 = new System.Windows.Forms.Button();
            this.button5 = new System.Windows.Forms.Button();
            this.button4 = new System.Windows.Forms.Button();
            this.listBox3 = new System.Windows.Forms.ListBox();
            this.richTextBox2 = new System.Windows.Forms.RichTextBox();
            this.tabPage4 = new System.Windows.Forms.TabPage();
            this.testPos = new System.Windows.Forms.Button();
            this.teachNullPos = new System.Windows.Forms.Button();
            this.moveRight = new System.Windows.Forms.Button();
            this.moveLeft = new System.Windows.Forms.Button();
            this.moveDown = new System.Windows.Forms.Button();
            this.moveUp = new System.Windows.Forms.Button();
            this.testPosSwitch = new System.Windows.Forms.Button();
            this.label10 = new System.Windows.Forms.Label();
            this.listBoxCamPosControl = new System.Windows.Forms.ListBox();
            this.teachCamPos = new System.Windows.Forms.Button();
            this.resetCamPos = new System.Windows.Forms.Button();
            this.moveCamPos = new System.Windows.Forms.Button();
            this.richTextBox3 = new System.Windows.Forms.RichTextBox();
            this.TabControl1.SuspendLayout();
            this.tabPage1.SuspendLayout();
            this.tabPage2.SuspendLayout();
            this.tabPage3.SuspendLayout();
            this.groupBox2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.trackBar4)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.trackBar3)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.trackBar2)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.trackBar1)).BeginInit();
            this.groupBox1.SuspendLayout();
            this.tabPage4.SuspendLayout();
            this.SuspendLayout();
            // 
            // TabControl1
            // 
            this.TabControl1.Controls.Add(this.tabPage1);
            this.TabControl1.Controls.Add(this.tabPage2);
            this.TabControl1.Controls.Add(this.tabPage3);
            this.TabControl1.Controls.Add(this.tabPage4);
            this.TabControl1.Location = new System.Drawing.Point(12, 36);
            this.TabControl1.Name = "TabControl1";
            this.TabControl1.SelectedIndex = 0;
            this.TabControl1.Size = new System.Drawing.Size(776, 531);
            this.TabControl1.TabIndex = 0;
            this.TabControl1.SelectedIndexChanged += new System.EventHandler(this.TabControl1_SelectedIndexChanged);
            // 
            // tabPage1
            // 
            this.tabPage1.Controls.Add(this.button15);
            this.tabPage1.Controls.Add(this.button1);
            this.tabPage1.Controls.Add(this.label4);
            this.tabPage1.Controls.Add(this.dateTimePicker1);
            this.tabPage1.Controls.Add(this.label3);
            this.tabPage1.Controls.Add(this.textBox2);
            this.tabPage1.Controls.Add(this.label2);
            this.tabPage1.Controls.Add(this.listBox1);
            this.tabPage1.Controls.Add(this.label1);
            this.tabPage1.Controls.Add(this.textBox1);
            this.tabPage1.Controls.Add(this.menuStrip1);
            this.tabPage1.Location = new System.Drawing.Point(4, 22);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage1.Size = new System.Drawing.Size(768, 505);
            this.tabPage1.TabIndex = 0;
            this.tabPage1.Text = "Event Timer";
            this.tabPage1.UseVisualStyleBackColor = true;
            // 
            // button15
            // 
            this.button15.BackColor = System.Drawing.Color.Red;
            this.button15.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F);
            this.button15.Location = new System.Drawing.Point(143, 360);
            this.button15.Name = "button15";
            this.button15.Size = new System.Drawing.Size(83, 33);
            this.button15.TabIndex = 10;
            this.button15.Text = "Reset";
            this.button15.UseVisualStyleBackColor = false;
            this.button15.Click += new System.EventHandler(this.button15_Click);
            // 
            // button1
            // 
            this.button1.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F);
            this.button1.Location = new System.Drawing.Point(143, 293);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(83, 31);
            this.button1.TabIndex = 9;
            this.button1.Text = "Start";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label4.Location = new System.Drawing.Point(18, 236);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(83, 22);
            this.label4.TabIndex = 8;
            this.label4.Text = "Eventzeit";
            // 
            // dateTimePicker1
            // 
            this.dateTimePicker1.CalendarFont = new System.Drawing.Font("Microsoft Sans Serif", 12F);
            this.dateTimePicker1.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F);
            this.dateTimePicker1.Format = System.Windows.Forms.DateTimePickerFormat.Time;
            this.dateTimePicker1.Location = new System.Drawing.Point(143, 236);
            this.dateTimePicker1.Name = "dateTimePicker1";
            this.dateTimePicker1.Size = new System.Drawing.Size(98, 27);
            this.dateTimePicker1.TabIndex = 7;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label3.Location = new System.Drawing.Point(18, 177);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(117, 22);
            this.label3.TabIndex = 6;
            this.label3.Text = "Pause Text 2";
            // 
            // textBox2
            // 
            this.textBox2.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.textBox2.Location = new System.Drawing.Point(143, 177);
            this.textBox2.Name = "textBox2";
            this.textBox2.Size = new System.Drawing.Size(595, 27);
            this.textBox2.TabIndex = 5;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.Location = new System.Drawing.Point(18, 122);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(117, 22);
            this.label2.TabIndex = 4;
            this.label2.Text = "Pause Text 1";
            // 
            // listBox1
            // 
            this.listBox1.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.listBox1.FormattingEnabled = true;
            this.listBox1.ItemHeight = 22;
            this.listBox1.Items.AddRange(new object[] {
            "Pause",
            "Timer"});
            this.listBox1.Location = new System.Drawing.Point(143, 53);
            this.listBox1.Name = "listBox1";
            this.listBox1.Size = new System.Drawing.Size(98, 48);
            this.listBox1.TabIndex = 3;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.Location = new System.Drawing.Point(18, 77);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(56, 22);
            this.label1.TabIndex = 2;
            this.label1.Text = "Event";
            // 
            // textBox1
            // 
            this.textBox1.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.textBox1.Location = new System.Drawing.Point(143, 122);
            this.textBox1.Name = "textBox1";
            this.textBox1.Size = new System.Drawing.Size(595, 27);
            this.textBox1.TabIndex = 1;
            // 
            // menuStrip1
            // 
            this.menuStrip1.ImageScalingSize = new System.Drawing.Size(18, 18);
            this.menuStrip1.Location = new System.Drawing.Point(3, 3);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(762, 24);
            this.menuStrip1.TabIndex = 11;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // tabPage2
            // 
            this.tabPage2.Controls.Add(this.button13);
            this.tabPage2.Controls.Add(this.button12);
            this.tabPage2.Controls.Add(this.button10);
            this.tabPage2.Controls.Add(this.listBox2);
            this.tabPage2.Controls.Add(this.button3);
            this.tabPage2.Controls.Add(this.button2);
            this.tabPage2.Controls.Add(this.label5);
            this.tabPage2.Controls.Add(this.richTextBox1);
            this.tabPage2.Location = new System.Drawing.Point(4, 22);
            this.tabPage2.Name = "tabPage2";
            this.tabPage2.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage2.Size = new System.Drawing.Size(768, 505);
            this.tabPage2.TabIndex = 1;
            this.tabPage2.Text = "Infrarot Fernbedienung";
            this.tabPage2.UseVisualStyleBackColor = true;
            // 
            // button13
            // 
            this.button13.BackColor = System.Drawing.Color.Red;
            this.button13.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F);
            this.button13.Location = new System.Drawing.Point(659, 239);
            this.button13.Name = "button13";
            this.button13.Size = new System.Drawing.Size(103, 32);
            this.button13.TabIndex = 7;
            this.button13.Text = "Reset";
            this.button13.UseVisualStyleBackColor = false;
            this.button13.Click += new System.EventHandler(this.button13_Click);
            // 
            // button12
            // 
            this.button12.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F);
            this.button12.Location = new System.Drawing.Point(659, 303);
            this.button12.Name = "button12";
            this.button12.Size = new System.Drawing.Size(103, 32);
            this.button12.TabIndex = 6;
            this.button12.Text = "Test Sequ";
            this.button12.UseVisualStyleBackColor = true;
            this.button12.Click += new System.EventHandler(this.button12_Click);
            // 
            // button10
            // 
            this.button10.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F);
            this.button10.Location = new System.Drawing.Point(659, 467);
            this.button10.Name = "button10";
            this.button10.Size = new System.Drawing.Size(103, 32);
            this.button10.TabIndex = 5;
            this.button10.Text = "Default";
            this.button10.UseVisualStyleBackColor = true;
            this.button10.Click += new System.EventHandler(this.button10_Click);
            // 
            // listBox2
            // 
            this.listBox2.FormattingEnabled = true;
            this.listBox2.Items.AddRange(new object[] {
            "HDMI Switch Laptop",
            "HDMI Switch GoPro Actionkamera",
            "HDMI Switch Camcorder Schwenkneiger",
            "HDMI Switch Camcorder Empore",
            "HDMI Switch Kombination Laptop + GoPro",
            "Beamer HDMI 1",
            "Beamer HDMI 2",
            "Beamer Analogeingang",
            "Beamer Muten",
            "Beamer ausschalten",
            "HDMI Switch ausschalten",
            "Backup Recorder ausschalten",
            "Backup Recorder Aufnahme start",
            "Backup Recorder Aufnahme stop",
            "Beamer einschalten"});
            this.listBox2.Location = new System.Drawing.Point(19, 43);
            this.listBox2.Name = "listBox2";
            this.listBox2.Size = new System.Drawing.Size(398, 199);
            this.listBox2.TabIndex = 4;
            // 
            // button3
            // 
            this.button3.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F);
            this.button3.Location = new System.Drawing.Point(342, 255);
            this.button3.Name = "button3";
            this.button3.Size = new System.Drawing.Size(75, 32);
            this.button3.TabIndex = 3;
            this.button3.Text = "Teach";
            this.button3.UseVisualStyleBackColor = true;
            this.button3.Click += new System.EventHandler(this.button3_Click);
            // 
            // button2
            // 
            this.button2.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F);
            this.button2.Location = new System.Drawing.Point(19, 255);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(75, 32);
            this.button2.TabIndex = 2;
            this.button2.Text = "Start";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.button2_Click);
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F);
            this.label5.Location = new System.Drawing.Point(15, 18);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(133, 22);
            this.label5.TabIndex = 1;
            this.label5.Text = "Infrarotsequenz";
            // 
            // richTextBox1
            // 
            this.richTextBox1.Location = new System.Drawing.Point(6, 303);
            this.richTextBox1.Name = "richTextBox1";
            this.richTextBox1.Size = new System.Drawing.Size(634, 196);
            this.richTextBox1.TabIndex = 0;
            this.richTextBox1.Text = "";
            // 
            // tabPage3
            // 
            this.tabPage3.Controls.Add(this.button11);
            this.tabPage3.Controls.Add(this.groupBox2);
            this.tabPage3.Controls.Add(this.groupBox1);
            this.tabPage3.Controls.Add(this.richTextBox2);
            this.tabPage3.Location = new System.Drawing.Point(4, 22);
            this.tabPage3.Name = "tabPage3";
            this.tabPage3.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage3.Size = new System.Drawing.Size(768, 505);
            this.tabPage3.TabIndex = 2;
            this.tabPage3.Text = "Audiomix";
            this.tabPage3.UseVisualStyleBackColor = true;
            // 
            // button11
            // 
            this.button11.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F);
            this.button11.Location = new System.Drawing.Point(643, 467);
            this.button11.Name = "button11";
            this.button11.Size = new System.Drawing.Size(122, 32);
            this.button11.TabIndex = 7;
            this.button11.Text = "Default";
            this.button11.UseVisualStyleBackColor = true;
            this.button11.Click += new System.EventHandler(this.button11_Click);
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.label9);
            this.groupBox2.Controls.Add(this.label8);
            this.groupBox2.Controls.Add(this.label7);
            this.groupBox2.Controls.Add(this.label6);
            this.groupBox2.Controls.Add(this.button16);
            this.groupBox2.Controls.Add(this.trackBar4);
            this.groupBox2.Controls.Add(this.trackBar3);
            this.groupBox2.Controls.Add(this.trackBar2);
            this.groupBox2.Controls.Add(this.trackBar1);
            this.groupBox2.Controls.Add(this.button14);
            this.groupBox2.Controls.Add(this.button9);
            this.groupBox2.Controls.Add(this.listBox4);
            this.groupBox2.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F);
            this.groupBox2.Location = new System.Drawing.Point(6, 6);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(377, 319);
            this.groupBox2.TabIndex = 4;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Audiosequenz";
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(237, 135);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(52, 22);
            this.label9.TabIndex = 13;
            this.label9.Text = "Kan4";
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(183, 135);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(57, 22);
            this.label8.TabIndex = 12;
            this.label8.Text = "Raum";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(136, 135);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(46, 22);
            this.label7.TabIndex = 11;
            this.label7.Text = "Sum";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(90, 135);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(35, 22);
            this.label6.TabIndex = 10;
            this.label6.Text = "PC";
            // 
            // button16
            // 
            this.button16.Location = new System.Drawing.Point(241, 280);
            this.button16.Name = "button16";
            this.button16.Size = new System.Drawing.Size(87, 28);
            this.button16.TabIndex = 9;
            this.button16.Text = "Teach";
            this.button16.UseVisualStyleBackColor = true;
            this.button16.Click += new System.EventHandler(this.button16_Click);
            // 
            // trackBar4
            // 
            this.trackBar4.Location = new System.Drawing.Point(187, 158);
            this.trackBar4.Name = "trackBar4";
            this.trackBar4.Orientation = System.Windows.Forms.Orientation.Vertical;
            this.trackBar4.Size = new System.Drawing.Size(50, 118);
            this.trackBar4.TabIndex = 8;
            this.trackBar4.Scroll += new System.EventHandler(this.trackBar4_Scroll);
            // 
            // trackBar3
            // 
            this.trackBar3.Location = new System.Drawing.Point(238, 158);
            this.trackBar3.Name = "trackBar3";
            this.trackBar3.Orientation = System.Windows.Forms.Orientation.Vertical;
            this.trackBar3.Size = new System.Drawing.Size(50, 118);
            this.trackBar3.TabIndex = 7;
            this.trackBar3.Scroll += new System.EventHandler(this.trackBar3_Scroll);
            // 
            // trackBar2
            // 
            this.trackBar2.Location = new System.Drawing.Point(136, 158);
            this.trackBar2.Name = "trackBar2";
            this.trackBar2.Orientation = System.Windows.Forms.Orientation.Vertical;
            this.trackBar2.Size = new System.Drawing.Size(50, 118);
            this.trackBar2.TabIndex = 6;
            this.trackBar2.Scroll += new System.EventHandler(this.trackBar2_Scroll);
            // 
            // trackBar1
            // 
            this.trackBar1.Location = new System.Drawing.Point(85, 158);
            this.trackBar1.Name = "trackBar1";
            this.trackBar1.Orientation = System.Windows.Forms.Orientation.Vertical;
            this.trackBar1.Size = new System.Drawing.Size(50, 118);
            this.trackBar1.TabIndex = 5;
            this.trackBar1.Scroll += new System.EventHandler(this.trackBar1_Scroll);
            // 
            // button14
            // 
            this.button14.BackColor = System.Drawing.Color.Red;
            this.button14.Location = new System.Drawing.Point(140, 279);
            this.button14.Name = "button14";
            this.button14.Size = new System.Drawing.Size(87, 31);
            this.button14.TabIndex = 4;
            this.button14.Text = "Reset";
            this.button14.UseVisualStyleBackColor = false;
            this.button14.Click += new System.EventHandler(this.button14_Click);
            // 
            // button9
            // 
            this.button9.Location = new System.Drawing.Point(38, 282);
            this.button9.Name = "button9";
            this.button9.Size = new System.Drawing.Size(87, 28);
            this.button9.TabIndex = 3;
            this.button9.Text = "Start";
            this.button9.UseVisualStyleBackColor = true;
            this.button9.Click += new System.EventHandler(this.button9_Click);
            // 
            // listBox4
            // 
            this.listBox4.Font = new System.Drawing.Font("Microsoft Sans Serif", 11F);
            this.listBox4.FormattingEnabled = true;
            this.listBox4.ItemHeight = 20;
            this.listBox4.Items.AddRange(new object[] {
            "Diashow",
            "Gottesdienst",
            "Predigt",
            "Text",
            "Band",
            "VideoClip"});
            this.listBox4.Location = new System.Drawing.Point(85, 26);
            this.listBox4.Name = "listBox4";
            this.listBox4.Size = new System.Drawing.Size(198, 104);
            this.listBox4.TabIndex = 0;
            this.listBox4.SelectedIndexChanged += new System.EventHandler(this.listBox4_SelectedIndexChanged);
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.button8);
            this.groupBox1.Controls.Add(this.button7);
            this.groupBox1.Controls.Add(this.button6);
            this.groupBox1.Controls.Add(this.button5);
            this.groupBox1.Controls.Add(this.button4);
            this.groupBox1.Controls.Add(this.listBox3);
            this.groupBox1.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F);
            this.groupBox1.Location = new System.Drawing.Point(389, 6);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(373, 319);
            this.groupBox1.TabIndex = 3;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Eingangssignale";
            // 
            // button8
            // 
            this.button8.Location = new System.Drawing.Point(208, 216);
            this.button8.Name = "button8";
            this.button8.Size = new System.Drawing.Size(87, 28);
            this.button8.TabIndex = 6;
            this.button8.Text = ">>>";
            this.button8.UseVisualStyleBackColor = true;
            this.button8.Click += new System.EventHandler(this.button8_Click);
            // 
            // button7
            // 
            this.button7.Location = new System.Drawing.Point(82, 216);
            this.button7.Name = "button7";
            this.button7.Size = new System.Drawing.Size(87, 28);
            this.button7.TabIndex = 5;
            this.button7.Text = "<<<";
            this.button7.UseVisualStyleBackColor = true;
            this.button7.Click += new System.EventHandler(this.button7_Click);
            // 
            // button6
            // 
            this.button6.Location = new System.Drawing.Point(208, 282);
            this.button6.Name = "button6";
            this.button6.Size = new System.Drawing.Size(87, 28);
            this.button6.TabIndex = 4;
            this.button6.Text = "Reset Audiomix";
            this.button6.UseVisualStyleBackColor = true;
            this.button6.Click += new System.EventHandler(this.button6_Click);
            // 
            // button5
            // 
            this.button5.Location = new System.Drawing.Point(208, 147);
            this.button5.Name = "button5";
            this.button5.Size = new System.Drawing.Size(87, 28);
            this.button5.TabIndex = 3;
            this.button5.Text = "Init";
            this.button5.UseVisualStyleBackColor = true;
            this.button5.Click += new System.EventHandler(this.button5_Click);
            // 
            // button4
            // 
            this.button4.Location = new System.Drawing.Point(82, 147);
            this.button4.Name = "button4";
            this.button4.Size = new System.Drawing.Size(87, 28);
            this.button4.TabIndex = 2;
            this.button4.Text = "aktivieren";
            this.button4.UseVisualStyleBackColor = true;
            this.button4.Click += new System.EventHandler(this.button4_Click);
            // 
            // listBox3
            // 
            this.listBox3.Font = new System.Drawing.Font("Microsoft Sans Serif", 11F);
            this.listBox3.FormattingEnabled = true;
            this.listBox3.ItemHeight = 20;
            this.listBox3.Items.AddRange(new object[] {
            "Laptop Audioausgang",
            "Summensignal Verstärker",
            "Raum Mikrofon",
            "Audiokanal 4"});
            this.listBox3.Location = new System.Drawing.Point(82, 40);
            this.listBox3.Name = "listBox3";
            this.listBox3.Size = new System.Drawing.Size(213, 84);
            this.listBox3.TabIndex = 1;
            this.listBox3.SelectedIndexChanged += new System.EventHandler(this.listBox3_SelectedIndexChanged);
            // 
            // richTextBox2
            // 
            this.richTextBox2.Location = new System.Drawing.Point(6, 331);
            this.richTextBox2.Name = "richTextBox2";
            this.richTextBox2.Size = new System.Drawing.Size(631, 168);
            this.richTextBox2.TabIndex = 0;
            this.richTextBox2.Text = "";
            // 
            // tabPage4
            // 
            this.tabPage4.Controls.Add(this.testPos);
            this.tabPage4.Controls.Add(this.teachNullPos);
            this.tabPage4.Controls.Add(this.moveRight);
            this.tabPage4.Controls.Add(this.moveLeft);
            this.tabPage4.Controls.Add(this.moveDown);
            this.tabPage4.Controls.Add(this.moveUp);
            this.tabPage4.Controls.Add(this.testPosSwitch);
            this.tabPage4.Controls.Add(this.label10);
            this.tabPage4.Controls.Add(this.listBoxCamPosControl);
            this.tabPage4.Controls.Add(this.teachCamPos);
            this.tabPage4.Controls.Add(this.resetCamPos);
            this.tabPage4.Controls.Add(this.moveCamPos);
            this.tabPage4.Controls.Add(this.richTextBox3);
            this.tabPage4.Location = new System.Drawing.Point(4, 22);
            this.tabPage4.Name = "tabPage4";
            this.tabPage4.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage4.Size = new System.Drawing.Size(768, 505);
            this.tabPage4.TabIndex = 3;
            this.tabPage4.Text = "Position Camcorder";
            this.tabPage4.UseVisualStyleBackColor = true;
            // 
            // testPos
            // 
            this.testPos.Location = new System.Drawing.Point(375, 297);
            this.testPos.Name = "testPos";
            this.testPos.Size = new System.Drawing.Size(107, 28);
            this.testPos.TabIndex = 19;
            this.testPos.Text = "Test";
            this.testPos.UseVisualStyleBackColor = true;
            this.testPos.Click += new System.EventHandler(this.testPos_Click);
            // 
            // teachNullPos
            // 
            this.teachNullPos.Location = new System.Drawing.Point(652, 297);
            this.teachNullPos.Name = "teachNullPos";
            this.teachNullPos.Size = new System.Drawing.Size(110, 28);
            this.teachNullPos.TabIndex = 18;
            this.teachNullPos.Text = "Teach Null Pos";
            this.teachNullPos.UseVisualStyleBackColor = true;
            this.teachNullPos.Click += new System.EventHandler(this.teachNullPos_Click);
            // 
            // moveRight
            // 
            this.moveRight.Image = ((System.Drawing.Image)(resources.GetObject("moveRight.Image")));
            this.moveRight.Location = new System.Drawing.Point(616, 105);
            this.moveRight.Name = "moveRight";
            this.moveRight.Size = new System.Drawing.Size(85, 85);
            this.moveRight.TabIndex = 17;
            this.moveRight.UseVisualStyleBackColor = true;
            this.moveRight.MouseDown += new System.Windows.Forms.MouseEventHandler(this.moveRightHandler);
            this.moveRight.MouseUp += new System.Windows.Forms.MouseEventHandler(this.moveDoneHandler);
            // 
            // moveLeft
            // 
            this.moveLeft.Image = ((System.Drawing.Image)(resources.GetObject("moveLeft.Image")));
            this.moveLeft.Location = new System.Drawing.Point(434, 105);
            this.moveLeft.Name = "moveLeft";
            this.moveLeft.Size = new System.Drawing.Size(85, 85);
            this.moveLeft.TabIndex = 16;
            this.moveLeft.UseVisualStyleBackColor = true;
            this.moveLeft.MouseDown += new System.Windows.Forms.MouseEventHandler(this.moveLeftHandler);
            this.moveLeft.MouseUp += new System.Windows.Forms.MouseEventHandler(this.moveDoneHandler);
            // 
            // moveDown
            // 
            this.moveDown.Image = ((System.Drawing.Image)(resources.GetObject("moveDown.Image")));
            this.moveDown.Location = new System.Drawing.Point(525, 197);
            this.moveDown.Name = "moveDown";
            this.moveDown.Size = new System.Drawing.Size(85, 85);
            this.moveDown.TabIndex = 15;
            this.moveDown.UseVisualStyleBackColor = true;
            this.moveDown.MouseDown += new System.Windows.Forms.MouseEventHandler(this.moveDownHandler);
            this.moveDown.MouseUp += new System.Windows.Forms.MouseEventHandler(this.moveDoneHandler);
            // 
            // moveUp
            // 
            this.moveUp.Image = ((System.Drawing.Image)(resources.GetObject("moveUp.Image")));
            this.moveUp.Location = new System.Drawing.Point(525, 18);
            this.moveUp.Name = "moveUp";
            this.moveUp.Size = new System.Drawing.Size(85, 85);
            this.moveUp.TabIndex = 14;
            this.moveUp.UseVisualStyleBackColor = true;
            this.moveUp.MouseDown += new System.Windows.Forms.MouseEventHandler(this.moveUpHandler);
            this.moveUp.MouseUp += new System.Windows.Forms.MouseEventHandler(this.moveDoneHandler);
            // 
            // testPosSwitch
            // 
            this.testPosSwitch.Location = new System.Drawing.Point(513, 297);
            this.testPosSwitch.Name = "testPosSwitch";
            this.testPosSwitch.Size = new System.Drawing.Size(108, 28);
            this.testPosSwitch.TabIndex = 13;
            this.testPosSwitch.Text = "Test Switch";
            this.testPosSwitch.UseVisualStyleBackColor = true;
            this.testPosSwitch.Click += new System.EventHandler(this.testPosSwitch_Click);
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F);
            this.label10.Location = new System.Drawing.Point(6, 3);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(183, 22);
            this.label10.TabIndex = 12;
            this.label10.Text = "Camcorder -  Position";
            // 
            // listBoxCamPosControl
            // 
            this.listBoxCamPosControl.FormattingEnabled = true;
            this.listBoxCamPosControl.Items.AddRange(new object[] {
            "Altar",
            "Taufstein",
            "Kanzel",
            "Orgel",
            "Mittelgang",
            "Position_6",
            "Position_7",
            "Position_8",
            "Position_9",
            "Position_10",
            "Position_11",
            "Position_12",
            "Position_13",
            "Position_14",
            "Position_15",
            "Position_16",
            "Position_17",
            "Position_18",
            "Position_19",
            "Position_20"});
            this.listBoxCamPosControl.Location = new System.Drawing.Point(10, 28);
            this.listBoxCamPosControl.Name = "listBoxCamPosControl";
            this.listBoxCamPosControl.Size = new System.Drawing.Size(325, 264);
            this.listBoxCamPosControl.TabIndex = 11;
            this.listBoxCamPosControl.SelectedIndexChanged += new System.EventHandler(this.listBoxCamPosControl_SelectedIndexChanged);
            // 
            // teachCamPos
            // 
            this.teachCamPos.Location = new System.Drawing.Point(248, 297);
            this.teachCamPos.Name = "teachCamPos";
            this.teachCamPos.Size = new System.Drawing.Size(87, 28);
            this.teachCamPos.TabIndex = 10;
            this.teachCamPos.Text = "Teach";
            this.teachCamPos.UseVisualStyleBackColor = true;
            this.teachCamPos.Click += new System.EventHandler(this.teachCamPos_Click);
            // 
            // resetCamPos
            // 
            this.resetCamPos.BackColor = System.Drawing.Color.Red;
            this.resetCamPos.Location = new System.Drawing.Point(129, 296);
            this.resetCamPos.Name = "resetCamPos";
            this.resetCamPos.Size = new System.Drawing.Size(87, 28);
            this.resetCamPos.TabIndex = 5;
            this.resetCamPos.Text = "Reset";
            this.resetCamPos.UseVisualStyleBackColor = false;
            this.resetCamPos.Click += new System.EventHandler(this.resetCamPos_Click);
            // 
            // moveCamPos
            // 
            this.moveCamPos.Location = new System.Drawing.Point(6, 297);
            this.moveCamPos.Name = "moveCamPos";
            this.moveCamPos.Size = new System.Drawing.Size(87, 28);
            this.moveCamPos.TabIndex = 4;
            this.moveCamPos.Text = "Start";
            this.moveCamPos.UseVisualStyleBackColor = true;
            this.moveCamPos.Click += new System.EventHandler(this.moveCamPos_Click);
            // 
            // richTextBox3
            // 
            this.richTextBox3.Location = new System.Drawing.Point(6, 341);
            this.richTextBox3.Name = "richTextBox3";
            this.richTextBox3.Size = new System.Drawing.Size(756, 154);
            this.richTextBox3.TabIndex = 0;
            this.richTextBox3.Text = "";
            this.richTextBox3.KeyDown += new System.Windows.Forms.KeyEventHandler(this.rtb3KeyDown);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 579);
            this.Controls.Add(this.TabControl1);
            this.MainMenuStrip = this.menuStrip1;
            this.Name = "Form1";
            this.Text = "Joki Automation";
            this.TabControl1.ResumeLayout(false);
            this.tabPage1.ResumeLayout(false);
            this.tabPage1.PerformLayout();
            this.tabPage2.ResumeLayout(false);
            this.tabPage2.PerformLayout();
            this.tabPage3.ResumeLayout(false);
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.trackBar4)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.trackBar3)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.trackBar2)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.trackBar1)).EndInit();
            this.groupBox1.ResumeLayout(false);
            this.tabPage4.ResumeLayout(false);
            this.tabPage4.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        public System.Windows.Forms.TabControl TabControl1;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.TabPage tabPage2;
        private System.Windows.Forms.TabPage tabPage3;
        private System.Windows.Forms.TabPage tabPage4;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ListBox listBox1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.DateTimePicker dateTimePicker1;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox textBox2;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Label label5;
        public  System.Windows.Forms.RichTextBox richTextBox1;
        private System.Windows.Forms.ListBox listBox2;
        private System.Windows.Forms.Button button3;
        private System.Windows.Forms.Button button2;
        public  System.Windows.Forms.RichTextBox richTextBox2;
        public  System.Windows.Forms.RichTextBox richTextBox3;
        private System.Windows.Forms.ListBox listBox3;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Button button7;
        private System.Windows.Forms.Button button6;
        private System.Windows.Forms.Button button5;
        private System.Windows.Forms.Button button4;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.Button button8;
        private System.Windows.Forms.Button button9;
        private System.Windows.Forms.ListBox listBox4;
        private System.Windows.Forms.Button button10;
        private System.Windows.Forms.Button button11;
        private System.Windows.Forms.Button button12;
        private System.Windows.Forms.Button button13;
        private System.Windows.Forms.Button button14;
        private System.Windows.Forms.Button button15;
        private System.Windows.Forms.TrackBar trackBar4;
        private System.Windows.Forms.TrackBar trackBar3;
        private System.Windows.Forms.TrackBar trackBar2;
        private System.Windows.Forms.TrackBar trackBar1;
        private System.Windows.Forms.Button button16;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Button teachCamPos;
        private System.Windows.Forms.Button resetCamPos;
        private System.Windows.Forms.Button moveCamPos;
        private System.Windows.Forms.Label label10;
        public System.Windows.Forms.ListBox listBoxCamPosControl;
        private System.Windows.Forms.Button testPosSwitch;
        private Button moveUp;
        private Button teachNullPos;
        private Button moveRight;
        private Button moveLeft;
        private Button moveDown;
        private Button testPos;
        private MenuStrip menuStrip1;
    }
}

