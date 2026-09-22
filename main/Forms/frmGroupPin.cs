using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using client.Classes;

namespace client.Forms
{
    public sealed class frmGroupPin : Form
    {
        private readonly string Revision, GroupId, ProgramsFolder, AppId;
        private readonly Func<string, IPinClient> Factory;
        private readonly CancellationTokenSource Lifetime = new CancellationTokenSource();
        private readonly Button RequestButton = new Button();
        private readonly Button ShortcutButton = new Button();
        private readonly Label Status = new Label();
        private bool Busy, CloseAfterOperation;

        public frmGroupPin(string Revision, string GroupId)
            : this(Revision, GroupId, Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Value => new NativePinClient(Value)) { }

        // Explicit dependencies keep test registration in a disposable folder and requests in a fake client.
        public frmGroupPin(string Revision, string GroupId, string ProgramsFolder, Func<string, IPinClient> Factory)
        {
            GroupPublishing.GetShortcut(Revision, GroupId);
            Category Group = OrganizerStore.LoadGroup(GroupId);
            this.Revision = Revision;
            this.GroupId = GroupId;
            this.ProgramsFolder = ProgramsFolder;
            this.Factory = Factory;
            AppId = "tjackenpacken.taskbarGroup.menu." + Group.AppIdKey;
            Text = "Pin " + Group.Name;
            ClientSize = new Size(580, 220);
            MinimumSize = new Size(520, 250);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10);
            var Layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 3, ColumnCount = 1 };
            Layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            Layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            Layout.Controls.Add(new Label { Dock = DockStyle.Fill,
                Text = "Pin “" + Group.Name + "”\nThis adds a Start-menu shortcut for the group and asks Windows to pin it. Windows may ask you to confirm. Keep notifications enabled." }, 0, 0);
            Status.Dock = DockStyle.Fill;
            Status.Text = "Choose Request pin, or use Show shortcut for the manual Windows action.";
            Layout.Controls.Add(Status, 0, 1);
            var Actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
            RequestButton.Text = "Request pin";
            RequestButton.AutoSize = true;
            RequestButton.Click += async delegate { await RequestPinAsync(); };
            ShortcutButton.Text = "Show shortcut";
            ShortcutButton.AutoSize = true;
            ShortcutButton.Click += delegate {
                LaunchResult Result = GroupPublishing.ShowShortcut(this.Revision, this.GroupId);
                Status.Text = Result.Success ? "Use the shortcut's Windows context menu to pin it, if offered." : Result.Error;
            };
            var CloseButton = new Button { Text = "Close", AutoSize = true };
            CloseButton.Click += delegate { Close(); };
            Actions.Controls.AddRange(new Control[] { RequestButton, ShortcutButton, CloseButton });
            Layout.Controls.Add(Actions, 0, 2);
            Controls.Add(Layout);
            FormClosing += delegate(object Sender, FormClosingEventArgs E) {
                if (!Busy) return;
                E.Cancel = true;
                CloseAfterOperation = true;
                Lifetime.Cancel();
                Status.Text = "Closing the request. Any pin already approved in Windows is retained.";
            };
            FormClosed += delegate { Lifetime.Dispose(); };
        }

        private async Task RequestPinAsync()
        {
            if (Busy || Lifetime.IsCancellationRequested) return;
            Busy = true;
            RequestButton.Enabled = ShortcutButton.Enabled = false;
            bool Pinned = false;
            try
            {
                // Recheck before creating a client or registering anything.
                GroupPublishing.GetShortcut(Revision, GroupId);
                GroupPublishing.RegisterStartEntry(Revision, GroupId, ProgramsFolder);
                using (IPinClient Client = Factory(AppId))
                {
                    Status.Text = "Checking Windows pin availability…";
                    PinStatus Current = await Client.GetStatusAsync(Lifetime.Token);
                    Lifetime.Token.ThrowIfCancellationRequested();
                    GroupPublishing.GetShortcut(Revision, GroupId);
                    if (Current.Pinned) { Pinned = true; Status.Text = "Windows reports this group is already pinned."; }
                    else if (!Current.Supported || !Current.Allowed)
                        Status.Text = "Windows cannot offer pinning now. The Start-menu shortcut is ready. Use Show shortcut, or retry later.";
                    else
                    {
                        Status.Text = "Complete the Windows pin confirmation, if shown.";
                        Pinned = await Client.RequestAsync(Handle, Lifetime.Token);
                        Status.Text = Pinned ? "Windows reports this group is pinned." :
                            "Windows did not confirm a pin. Your group and Start-menu shortcut are still available.";
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Status.Text = "The request was cancelled. Check the taskbar before retrying; your group is unchanged.";
            }
            catch (Exception Error) when (NativePinClient.IsExpectedError(Error))
            {
                MainPath.Log(Error.Message, "Publishing");
                Status.Text = Error.Message + " Use Show shortcut for the manual Windows action.";
            }
            finally
            {
                Busy = false;
                if (!IsDisposed)
                {
                    RequestButton.Enabled = !Pinned && !Lifetime.IsCancellationRequested;
                    ShortcutButton.Enabled = true;
                    if (CloseAfterOperation) Close();
                }
            }
        }
    }
}
