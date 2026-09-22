using client.Classes;
using client.User_controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace client
{

    public partial class frmMain : Form
    {

        // Allow doubleBuffering drawing each frame to memory and then onto screen
        // Solves flickering issues mostly as the entire rendering of the screen is done in 1 operation after being first loaded to memory
        protected override CreateParams CreateParams

        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;
                return cp;
            }
        }

        public Category ThisCategory;
        public List<ucShortcut> ControlList;
        public Color HoverColor;

        private string passedDirec;
        public Point mouseClick;

        //------------------------------------------------------------------------------------
        // CTOR AND LOAD
        //
        public frmMain(string passedDirectory, int cursorPosX, int cursorPosY)
        {
            InitializeComponent();

            System.Runtime.ProfileOptimization.StartProfile("frmMain.Profile");
            mouseClick = new Point(cursorPosX, cursorPosY); // Consstruct point p based on passed x y mouse values
            passedDirec = passedDirectory;
            FormBorderStyle = FormBorderStyle.None;

            ThisCategory = new Category(passedDirec);
            using (MemoryStream Buffer = new MemoryStream(File.ReadAllBytes(Path.Combine(ThisCategory.ResourceDirectory, "GroupIcon.ico"))))
                this.Icon = new Icon(Buffer);

            {
                ControlList = new List<ucShortcut>();
                this.SetStyle(ControlStyles.SupportsTransparentBackColor, true);
                this.BackColor = ImageFunctions.FromString(ThisCategory.ColorString);
                Opacity = (1 - (ThisCategory.Opacity / 100));

                HoverColor = ImageFunctions.HoverColor(BackColor);
            }

        }

        private void frmMain_Load(object sender, EventArgs e)
        {
            LoadCategory();
            SetLocation();
        }

        private void SetLocation()
        {
            // Screen.FromPoint also selects the nearest monitor for stale/off-screen anchors.
            Screen Display = Screen.FromPoint(mouseClick);
            StartPosition = FormStartPosition.Manual;
            Location = PopupPlacement.GetLocation(Display.Bounds, Display.WorkingArea, mouseClick, Size);
        }

        // Loading category and building shortcuts
        private void LoadCategory()
        {
            //System.Diagnostics.Debugger.Launch();

            this.Width = 0;
            this.Height = 45;
            int x = 0;
            int y = 0;
            int width = ThisCategory.Width;
            int columns = 1;

            foreach (ProgramShortcut psc in ThisCategory.ShortcutList)
            {

                if (columns > width)  // creating new row if there are more psc than max width
                {
                    x = 0;
                    y += 45;
                    this.Height += 45;
                    columns = 1;
                }

                if (this.Width < ((width * 55)))
                    this.Width += (55);

                // OLD
                //BuildShortcutPanel(x, y, psc);
                
                // Building shortcut controls
                ucShortcut pscPanel = new ucShortcut() 
                {
                    Psc = psc, 
                    MotherForm = this, 
                    ThisCategory = ThisCategory 
                };
                pscPanel.Location = new System.Drawing.Point(x, y);
                this.Controls.Add(pscPanel);
                this.ControlList.Add(pscPanel);
                pscPanel.Show();
                pscPanel.BringToFront();

                // Reset values
                x += 55;
                columns++;
            }

            this.Width -= 2; // For some reason the width is 2 pixels larger than the shortcuts. Temporary fix
        }

        private Label LaunchStatus;
        private bool Launching;
        private bool DeactivatedDuringLaunch;

        public LaunchResult LaunchItem(ProgramShortcut Item)
        {
            LaunchResult Result = LaunchService.Launch(Item);
            if (!Result.Success)
            {
                if (LaunchStatus == null)
                {
                    int OriginalHeight = Height;
                    Width = Math.Max(Width, 320);
                    Height += 52;
                    LaunchStatus = new Label { Left = 6, Top = OriginalHeight + 4,
                        Width = Width - 12, Height = 44, ForeColor = Color.White,
                        BackColor = Color.FromArgb(45, 45, 45), AutoEllipsis = true };
                    Controls.Add(LaunchStatus);
                }
                LaunchStatus.Text = "Could not open item: " + Result.Error;
                SetLocation();
            }
            return Result;
        }

        // Retained for callers of the original API; all dispatch uses the same policy.
        public void OpenFile(string Arguments, string PathName, string WorkingDirectory)
        {
            LaunchItem(new ProgramShortcut { FilePath = PathName, Arguments = Arguments, WorkingDirectory = WorkingDirectory });
        }

        private void frmMain_Deactivate(object Sender, EventArgs E)
        {
            if (Launching) DeactivatedDuringLaunch = true;
            else Close();
        }

        private static int KeyIndex(Keys Key)
        {
            if (Key >= Keys.D1 && Key <= Keys.D9) return Key - Keys.D1;
            return Key == Keys.D0 ? 9 : -1;
        }

        private void frmMain_KeyDown(object Sender, KeyEventArgs E)
        {
            int Index = KeyIndex(E.KeyCode);
            if (Index >= 0 && Index < ControlList.Count)
                ControlList[Index].ucShortcut_MouseEnter(Sender, E);
        }

        private void frmMain_KeyUp(object Sender, KeyEventArgs E)
        {
            if (E.Modifiers == Keys.Control && E.KeyCode == Keys.Enter && ThisCategory.allowOpenAll)
            {
                Launching = true;
                DeactivatedDuringLaunch = false;
                bool Failed = false;
                try
                {
                    foreach (ucShortcut Item in ControlList)
                        if (!LaunchItem(Item.Psc).Success) Failed = true;
                }
                finally { Launching = false; }
                // Never reactivate or steal focus to display a batch error.
                if (DeactivatedDuringLaunch && !Failed) Close();
                return;
            }
            int Index = KeyIndex(E.KeyCode);
            if (Index >= 0 && Index < ControlList.Count)
            {
                ControlList[Index].ucShortcut_MouseLeave(Sender, E);
                ControlList[Index].ucShortcut_Click(Sender, E);
            }
        }
    }
}
