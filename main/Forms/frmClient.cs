using client.Classes;
using client.User_controls;
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Data.Json;
using System.Net.Http;
using System.Threading;

namespace client.Forms
{
    public partial class frmClient : Form
    {
        private static readonly HttpClient VersionClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 65536 };
        private readonly CancellationTokenSource VersionCancellation = new CancellationTokenSource();
        internal static Func<CancellationToken, Task<string>> VersionLookupOverride = null;
        internal static int VersionTimeoutMilliseconds = 5000;
        private bool VersionStopped;
        private readonly StatusStrip StorageStatus = new StatusStrip();
        private readonly ToolStripStatusLabel StorageMessage = new ToolStripStatusLabel();

        public frmClient()
        {
            System.Runtime.ProfileOptimization.StartProfile("frmClient.Profile");
            InitializeComponent();
            StorageStatus.Items.Add(StorageMessage);
            Controls.Add(StorageStatus);
            StorageStatus.BringToFront();
            this.MaximumSize = new Size(Screen.PrimaryScreen.WorkingArea.Width, Screen.PrimaryScreen.WorkingArea.Height);
            Reload();

            currentVersion.Text = "v" + System.Reflection.Assembly.GetEntryAssembly().GetName().Version.ToString();

            githubVersion.Text = "Checking…";
            Shown += CheckVersion;
            Disposed += delegate {
                if (!VersionStopped) { VersionStopped = true; VersionCancellation.Cancel(); VersionCancellation.Dispose(); }
            };
        }
        public void Reload()
        {
            // flush and reload existing groups
            while (pnlExistingGroups.Controls.Count > 0) pnlExistingGroups.Controls[0].Dispose();
            pnlExistingGroups.Height = 0;

            foreach (Category Group in GroupStore.LoadAll())
            {
                try
                {
                    GroupStore.RepairLink(Group);
                    LoadCategory(Group);
                }
                catch (Exception Error) when (GroupStore.IsDataError(Error))
                {
                    MainPath.Warn("Could not display group: " + Error.Message);
                }
            }

            if (pnlExistingGroups.HasChildren) // helper if no group is created
            {
                lblHelpTitle.Text = "Click on a group to add a taskbar shortcut";
                pnlHelp.Visible = true;
            }
            else // helper if groups are created
            {
                lblHelpTitle.Text = "Press on \"Add Taskbar group\" to get started";
                pnlHelp.Visible = false;
            }
            pnlBottomMain.Top = pnlExistingGroups.Bottom + 20; // spacing between existing groups and add new group btn

            StorageStatus.Visible = MainPath.StorageWarnings.Count > 0;
            StorageMessage.Text = "Storage warnings; see " + Path.Combine(MainPath.LogDirectory, "Storage.log");
            Reset();
        }

        public void LoadCategory(string dir)
        {
            LoadCategory(new Category(dir));
        }

        private void LoadCategory(Category category)
        {
            ucCategoryPanel newCategory = new ucCategoryPanel(this, category);
            pnlExistingGroups.Height += newCategory.Height;
            pnlExistingGroups.Controls.Add(newCategory);
            newCategory.Top = pnlExistingGroups.Height - newCategory.Height;
            newCategory.Show();
            newCategory.BringToFront();
            newCategory.MouseEnter += new System.EventHandler((sender, e) => EnterControl(sender, e, newCategory));
            newCategory.MouseLeave += new System.EventHandler((sender, e) => LeaveControl(sender, e, newCategory));
        }

        public void Reset()
        {
            if (pnlBottomMain.Bottom > this.Bottom)
                pnlLeftColumn.Height = pnlBottomMain.Bottom;
            else
                pnlLeftColumn.Height = this.RectangleToScreen(this.ClientRectangle).Height; // making left column pnl dynamic
        }

        private void cmdAddGroup_Click(object sender, EventArgs e)
        {
            frmGroup newGroup = new frmGroup(this);
            newGroup.Show();
            newGroup.BringToFront();
        }

        private void pnlAddGroup_MouseLeave(object sender, EventArgs e)
        {
            pnlAddGroup.BackColor = Color.FromArgb(3, 3, 3);
        }

        private void pnlAddGroup_MouseEnter(object sender, EventArgs e)
        {
            pnlAddGroup.BackColor = Color.FromArgb(31, 31, 31);
        }

        public void EnterControl(object sender, EventArgs e, Control control)
        {
            control.BackColor = Color.FromArgb(31, 31, 31);
        }
        public void LeaveControl(object sender, EventArgs e, Control control)
        {
            control.BackColor = Color.FromArgb(3, 3, 3);
        }

        private async void CheckVersion(object Sender, EventArgs E)
        {
            CancellationToken Token = VersionCancellation.Token;
            try
            {
                Task<string> Lookup = VersionLookupOverride == null ? GetVersionData(Token) : VersionLookupOverride(Token);
                Task Timeout = Task.Delay(VersionTimeoutMilliseconds, Token);
                if (await Task.WhenAny(Lookup, Timeout) != Lookup)
                {
                    // Observe a later fault even if a custom/slow lookup ignores cancellation.
                    ObserveVersionFailure(Lookup);
                    if (!Token.IsCancellationRequested && !IsDisposed) githubVersion.Text = "Unavailable";
                    if (!VersionStopped) VersionCancellation.Cancel();
                    return;
                }
                string Version = await Lookup;
                if (!Token.IsCancellationRequested && !IsDisposed) githubVersion.Text = Version;
            }
            catch (Exception Error) when (Error is HttpRequestException || Error is OperationCanceledException ||
                GroupStore.IsDataError(Error))
            {
                if (!Token.IsCancellationRequested && !IsDisposed)
                {
                    githubVersion.Text = "Unavailable";
                    MainPath.Log("Release check failed: " + Error.Message, "Updates");
                }
            }
        }

        private static async void ObserveVersionFailure(Task<string> Lookup)
        {
            try { await Lookup.ConfigureAwait(false); }
            catch (Exception Error) when (Error is HttpRequestException || Error is OperationCanceledException || GroupStore.IsDataError(Error))
            { MainPath.Log("Release check ended: " + Error.Message, "Updates"); }
        }

        private static async Task<string> GetVersionData(CancellationToken Token)
        {
            using (HttpRequestMessage Request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/tjackenpacken/taskbar-groups/releases/latest"))
            {
                Request.Headers.UserAgent.ParseAdd("taskbar-groups");
                using (HttpResponseMessage Response = await VersionClient.SendAsync(Request, Token).ConfigureAwait(false))
                {
                    Response.EnsureSuccessStatusCode();
                    string Body = await Response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return ParseVersion(Body);
                }
            }
        }

        internal static string ParseVersion(string Body)
        {
            JsonObject Data;
            IJsonValue Value;
            if (!JsonObject.TryParse(Body, out Data) || !Data.TryGetValue("tag_name", out Value) || Value.ValueType != JsonValueType.String)
                throw new InvalidDataException("Release response has no valid version tag.");
            string Version = Value.GetString();
            if (string.IsNullOrWhiteSpace(Version) || Version.Length > 100)
                throw new InvalidDataException("Release version tag is invalid.");
            return Version;
        }

        public void ShowError(string Message)
        {
            MainPath.Log(Message, "UI");
            StorageMessage.Text = Message;
            StorageStatus.Visible = true;
        }

        private void githubLink_LinkClicked(object Sender, LinkLabelLinkClickedEventArgs E)
        {
            LaunchResult Result = LaunchService.Launch(new ProgramShortcut { FilePath = "https://github.com/tjackenpacken/taskbar-groups/releases", WorkingDirectory = "" });
            if (!Result.Success) ShowError(Result.Error);
        }

        private void frmClient_Resize(object sender, EventArgs e)
        {
            Reset();
        }
    }
}
