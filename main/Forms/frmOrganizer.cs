using client.Classes;
using System;
using System.Drawing;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace client.Forms
{
    public sealed class frmOrganizer : Form
    {
        private readonly FlowLayoutPanel LayoutGrid = new FlowLayoutPanel();
        private readonly FlowLayoutPanel MemberGrid = new FlowLayoutPanel();
        private readonly Label MemberTitle = new Label();
        private readonly Label Status = new Label();
        private readonly Button UndoButton = new Button(), RedoButton = new Button();
        private readonly ContextMenuStrip TileMenu = new ContextMenuStrip();
        private readonly FlowLayoutPanel Customize = new FlowLayoutPanel();
        private readonly TextBox NameEditor = new TextBox();
        private RowStyle MemberHeaderRow, MemberRow, CustomizeRow;
        private string RenameId, RenameRevision;
        private readonly string Session = Guid.NewGuid().ToString("N");
        private OrganizerDocument Document;
        private string SelectedId;
        private string FocusedId;
        private Button PreviewTile;
        private int PreviewRegion;
        private bool Dragging;
        private ContextMenuStrip GroupMenu;
        private readonly Panel PinGuide = new Panel();
        private readonly Label PinInstructions = new Label();
        private RowStyle PinRow;
        private string PinGroupId, PinRevision;

        // A private in-process drag payload; external files use the separate import path.
        private sealed class DragPayload
        {
            public string Session, Revision, Id, Parent;
            public bool IsGroup;
        }
        private sealed class TileInfo
        {
            public string Id, Parent, Revision;
            public bool IsGroup;
            public int Index;
        }

        private readonly string PinnedFolder;
        private readonly bool IncludeRunning;

        public frmOrganizer() : this(null) { }

        public frmOrganizer(string PinnedFolder) : this(PinnedFolder, false) { }

        public frmOrganizer(string PinnedFolder, bool IncludeRunning)
        {
            this.PinnedFolder = PinnedFolder;
            this.IncludeRunning = IncludeRunning;
            Text = "Taskbar Groups";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(720, 320);
            ClientSize = new Size(950, 350);
            BackColor = Color.FromArgb(22, 24, 30);
            ForeColor = Color.WhiteSmoke;
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;

            TableLayoutPanel Root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(20) };
            Root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            Root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            Root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            MemberHeaderRow = new RowStyle(SizeType.Absolute, 0);
            MemberRow = new RowStyle(SizeType.Absolute, 0);
            CustomizeRow = new RowStyle(SizeType.Absolute, 0);
            Root.RowStyles.Add(MemberHeaderRow);
            Root.RowStyles.Add(MemberRow);
            Root.RowStyles.Add(CustomizeRow);
            PinRow = new RowStyle(SizeType.Absolute, 0);
            Root.RowStyles.Add(PinRow);
            Root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Controls.Add(Root);
            FlowLayoutPanel Tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            Root.Controls.Add(Tools, 0, 0);
            AddButton(Tools, new Button(), "Add apps", AddFiles);
            AddButton(Tools, UndoButton, "Undo", delegate { Change(() => OrganizerStore.Undo(Document.Revision), "Undone"); });
            AddButton(Tools, RedoButton, "Redo", delegate { Change(() => OrganizerStore.Redo(Document.Revision), "Redone"); });
            Label Hint = new Label { Dock = DockStyle.Fill, Text = "Drag an app onto another to group. Drag between apps to reorder. Right-click for optional actions.\nAlt+←/→ moves the focused app; Ctrl+Shift+M moves it out. Ctrl+Z undoes; F5 refreshes apps.",
                ForeColor = Color.FromArgb(180, 189, 205), AutoEllipsis = true };
            Root.Controls.Add(Hint, 0, 1);
            ConfigureGrid(LayoutGrid);
            LayoutGrid.WrapContents = false;
            Root.Controls.Add(LayoutGrid, 0, 2);
            MemberTitle.Dock = DockStyle.Fill;
            Panel MemberHeader = new Panel { Dock = DockStyle.Fill };
            MemberTitle.AutoEllipsis = true;
            MemberHeader.Controls.Add(MemberTitle);
            Root.Controls.Add(MemberHeader, 0, 3);
            ConfigureGrid(MemberGrid);
            MemberGrid.WrapContents = false;
            Root.Controls.Add(MemberGrid, 0, 4);
            Customize.Dock = DockStyle.Fill;
            Customize.Visible = false;
            NameEditor.Width = 240;
            NameEditor.AccessibleName = "Group name";
            Customize.Controls.Add(NameEditor);
            AddButton(Customize, new Button(), "Save name", SaveRename);
            AddButton(Customize, new Button(), "Cancel", delegate { HideCustomization(); });
            Root.Controls.Add(Customize, 0, 5);
            TileMenu.Opening += BuildTileMenu;
            PinGuide.Dock = DockStyle.Fill;
            PinGuide.Visible = false;
            PinInstructions.Dock = DockStyle.Top;
            PinInstructions.Height = 52;
            PinInstructions.AutoEllipsis = true;
            PinGuide.Controls.Add(PinInstructions);
            FlowLayoutPanel PinActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38 };
            AddButton(PinActions, new Button(), "Windows pin request (preview)…", OpenPinWindow);
            AddButton(PinActions, new Button(), "Show shortcut", ShowPinShortcut);
            AddButton(PinActions, new Button(), "Close", delegate { HidePinGuide(); });
            PinGuide.Controls.Add(PinActions);
            Root.Controls.Add(PinGuide, 0, 6);
            Status.Dock = DockStyle.Fill;
            Status.ForeColor = Color.FromArgb(169, 194, 224);
            Status.AutoEllipsis = true;
            Root.Controls.Add(Status, 0, 7);
            LayoutGrid.DragEnter += BackgroundOver;
            LayoutGrid.DragOver += BackgroundOver;
            LayoutGrid.DragDrop += BackgroundDrop;
            MemberGrid.DragEnter += MembersOver;
            MemberGrid.DragOver += MembersOver;
            MemberGrid.DragDrop += MembersDrop;
            KeyDown += OnOrganizerKey;
            Disposed += delegate { if (GroupMenu != null) GroupMenu.Dispose(); TileMenu.Dispose(); };
            // Activation is safe here because this surface displays both groups and standalone items.
            try
            {
                Document = OrganizerStore.Initialize();
                string Message = "Drag to group or reorder. Windows taskbar order stays separate.";
                if (PinnedFolder != null)
                {
                    try
                    {
                        OrganizerImportResult Imported = PinnedApps.ImportTaskbar(Document, PinnedFolder, IncludeRunning ? RunningApps.Read() : new List<ProgramShortcut>());
                        Document = Imported.Document;
                        Message = "Taskbar apps: " + Imported.Added + " added, " + Imported.Skipped + " skipped. Windows taskbar order stays separate.";
                    }
                    catch (Exception Error) when (IconService.IsExpectedError(Error))
                    { MainPath.Warn("[TaskbarGroups:PinnedImport] " + Error.Message); Message = "Pinned import unavailable. Your saved layout is ready; retry on restart."; }
                }
                Render(); SavedStatus(Message);
            }
            catch (Exception Error) when (IconService.IsExpectedError(Error)) { ShowError(Error); }
        }

        private void AddButton(Control Parent, Button Button, string Text, EventHandler Action)
        {
            Button.Text = Text;
            Button.AutoSize = true;
            Button.Height = 34;
            Button.FlatStyle = FlatStyle.Flat;
            Button.Margin = new Padding(0, 0, 8, 0);
            Button.Padding = new Padding(8, 2, 8, 2);
            Button.AccessibleName = Text;
            Button.Click += Action;
            Parent.Controls.Add(Button);
        }

        private void ConfigureGrid(FlowLayoutPanel Grid)
        {
            Grid.Dock = DockStyle.Fill;
            Grid.AutoScroll = true;
            Grid.Padding = new Padding(8);
            Grid.BackColor = Color.FromArgb(30, 33, 41);
            Grid.AllowDrop = true;
            Grid.DragLeave += delegate { ClearPreview(); };
        }

        private void SetStatus(string Text) { Status.Text = Text; }
        private void SavedStatus(string Message)
        {
            SetStatus(Document != null && Document.Recovered ? "Recovered previous layout; check Storage.log." :
                MainPath.StorageWarnings.Count > 0 ? Message + " Storage warnings: see Storage.log." : Message);
        }
        private void ShowError(Exception Error)
        {
            MainPath.Log(Error.Message, "Organizer");
            SetStatus("Could not complete action: " + Error.Message);
        }

        private void Change(Func<OrganizerDocument> Operation, string Message)
        {
            if (Document == null) return;
            try
            {
                HashSet<string> Existing = new HashSet<string>(Document.State.Groups.Select(Value => Value.Id));
                Document = Operation(); Render(); SavedStatus(Message);
                OrganizerGroup Created = Document.State.Groups.FirstOrDefault(Value => !Existing.Contains(Value.Id) && !Value.Deleted && Value.RedirectItemId == null);
                if (Created != null) { SelectedId = Created.Id; Render(); ShowPinGuide(this, EventArgs.Empty); }
            }
            catch (Exception Error) when (IconService.IsExpectedError(Error))
            {
                try { Document = OrganizerStore.Load(); Render(); }
                catch (Exception ReadError) when (IconService.IsExpectedError(ReadError)) { MainPath.Log(ReadError.Message, "Organizer"); }
                ShowError(Error);
            }
        }

        private void ReloadDocument()
        {
            try
            {
                Document = OrganizerStore.IsActive ? OrganizerStore.Load() : OrganizerStore.Initialize();
                if (PinnedFolder != null) Document = PinnedApps.ImportTaskbar(Document, PinnedFolder, IncludeRunning ? RunningApps.Read() : new List<ProgramShortcut>()).Document;
                Render();
                SetStatus("Refreshed taskbar apps and saved layout. Windows taskbar order stays separate.");
            }
            catch (Exception Error) when (IconService.IsExpectedError(Error)) { ShowError(Error); }
        }

        private void Render()
        {
            ClearPreview();
            string KeepFocus = FocusedId;
            LayoutGrid.SuspendLayout(); MemberGrid.SuspendLayout();
            try
            {
                while (LayoutGrid.Controls.Count > 0) LayoutGrid.Controls[0].Dispose();
                while (MemberGrid.Controls.Count > 0) MemberGrid.Controls[0].Dispose();
                if (Document == null) return;
                OrganizerStore.RepairLinks(Document);
                for (int Index = 0; Index < Document.State.Layout.Count; Index++)
                {
                    OrganizerEntry Entry = Document.State.Layout[Index];
                    LayoutGrid.Controls.Add(Tile(new TileInfo { Id = Entry.Id, IsGroup = Entry.IsGroup, Index = Index }));
                }
                OrganizerGroup Group = Document.State.Groups.SingleOrDefault(Value => Value.Id == SelectedId && !Value.Deleted && Value.RedirectItemId == null);
                MemberHeaderRow.Height = Group == null ? 0 : 30;
                MemberRow.Height = Group == null ? 0 : 138;
                MemberGrid.Visible = Group != null;
                if (Group == null || Group.Id != RenameId || Document.Revision != RenameRevision) HideCustomization();
                MemberTitle.Text = Group == null ? "Select a group to view its members" : Group.Name + "  •  Drag an app back to the top grid to remove it";
                if (Group != null)
                    for (int Index = 0; Index < Group.Members.Count; Index++)
                        MemberGrid.Controls.Add(Tile(new TileInfo { Id = Group.Members[Index], Parent = Group.Id, Index = Index }));
                UndoButton.Enabled = Document.Undo.Count > 0;
                RedoButton.Enabled = Document.Redo.Count > 0;
                if (Group == null || Group.Id != PinGroupId || Document.Revision != PinRevision) HidePinGuide();
                ResizeStrip();
                if (Document.Recovered) SetStatus("Recovered the previous saved layout. Check the storage log.");
            }
            finally { LayoutGrid.ResumeLayout(); MemberGrid.ResumeLayout(); FocusedId = KeepFocus; }
        }

        private void ResizeStrip()
        {
            int Height = 350 + (int)(MemberHeaderRow.Height + MemberRow.Height + CustomizeRow.Height + PinRow.Height);
            ClientSize = new Size(ClientSize.Width, Height);
        }

        private Button Tile(TileInfo Info)
        {
            Info.Revision = Document.Revision;
            string Name;
            Image Artwork;
            if (Info.IsGroup)
            {
                OrganizerGroup Group = Document.State.Groups.Single(Value => Value.Id == Info.Id);
                Name = Group.Name + Environment.NewLine + Group.Members.Count + " apps";
                try { Artwork = GroupStore.Load(Info.Id).LoadIconImage(); }
                catch (Exception Error) when (IconService.IsExpectedError(Error))
                { MainPath.Log(Error.Message, "Icons"); Artwork = new Bitmap(global::client.Properties.Resources.Error); }
            }
            else
            {
                ProgramShortcut Item = Document.State.Items.Single(Value => Value.Id == Info.Id);
                Name = IconService.GetName(Item);
                Artwork = IconService.GetIcon(Item);
            }
            Bitmap Icon;
            using (Artwork) Icon = ImageFunctions.ResizeImage(Artwork, 36, 36);
            Button Result = new Button { Text = Name, Image = Icon, Size = new Size(132, 104), Margin = new Padding(5),
                FlatStyle = FlatStyle.Flat, TextImageRelation = TextImageRelation.ImageAboveText, BackColor = Color.FromArgb(40, 44, 55),
                ForeColor = ForeColor, AllowDrop = true, Tag = Info, AccessibleName = Name, ContextMenuStrip = TileMenu,
                AccessibleDescription = Info.IsGroup ? "Select to view group members. Drag at edges to reorder." : "Drag onto another app or group to group. Press Enter or right-click to open." };
            Result.FlatAppearance.BorderColor = Info.Id == SelectedId ? Color.FromArgb(109, 176, 255) : Color.FromArgb(65, 72, 87);
            Result.Disposed += delegate { Icon.Dispose(); };
            Result.Enter += delegate { FocusedId = Info.Id; };
            Result.Click += delegate {
                if (Dragging) return;
                FocusedId = Info.Id;
                SelectedId = Info.Parent ?? Info.Id;
                Render();
            };
            Point Start = Point.Empty;
            bool Pressed = false;
            Result.MouseDown += delegate(object Sender, MouseEventArgs E) {
                if (E.Button == MouseButtons.Left) { Start = E.Location; Pressed = true; }
            };
            Result.MouseUp += delegate { Pressed = false; };
            Result.MouseMove += delegate(object Sender, MouseEventArgs E) {
                Size Threshold = SystemInformation.DragSize;
                if (!Pressed || E.Button != MouseButtons.Left ||
                    new Rectangle(Start.X - Threshold.Width / 2, Start.Y - Threshold.Height / 2, Threshold.Width, Threshold.Height).Contains(E.Location)) return;
                Pressed = false;
                DragPayload Payload = CreatePayload(Info);
                Dragging = true;
                try { Result.DoDragDrop(Payload, DragDropEffects.Move); }
                finally { Dragging = false; ClearPreview(); }
            };
            Result.QueryContinueDrag += delegate(object Sender, QueryContinueDragEventArgs E) {
                if (E.EscapePressed) { E.Action = DragAction.Cancel; ClearPreview(); SetStatus("Drag cancelled."); }
            };
            Result.DragEnter += TileOver;
            Result.DragOver += TileOver;
            Result.DragDrop += TileDrop;
            Result.DragLeave += delegate { ClearPreview(); };
            Result.Paint += delegate(object Sender, PaintEventArgs E) {
                if (PreviewTile != Result) return;
                using (Pen Pen = new Pen(Color.FromArgb(109, 176, 255), 4))
                {
                    if (PreviewRegion == 0) E.Graphics.DrawRectangle(Pen, 2, 2, Result.Width - 5, Result.Height - 5);
                    else { int X = PreviewRegion < 0 ? 3 : Result.Width - 4; E.Graphics.DrawLine(Pen, X, 3, X, Result.Height - 4); }
                }
            };
            return Result;
        }

        private DragPayload CreatePayload(TileInfo Info)
        {
            return new DragPayload { Id = Info.Id, Parent = Info.Parent, IsGroup = Info.IsGroup,
                Session = Session, Revision = Document.Revision };
        }
        private DragPayload Payload(IDataObject Data)
        {
            DragPayload Value = Data.GetData(typeof(DragPayload)) as DragPayload;
            return Value != null && Value.Session == Session ? Value : null;
        }

        // -1/+1 are insertion zones; 0 is the grouping zone.
        internal static int DropRegion(int X, int Width)
        { return X < Width / 4 ? -1 : X >= Width * 3 / 4 ? 1 : 0; }

        private void ClearPreview()
        {
            Button Previous = PreviewTile; PreviewTile = null;
            if (Previous != null && !Previous.IsDisposed) Previous.Invalidate();
        }

        private bool CanDrop(DragPayload Source, TileInfo Target, int Region)
        {
            if (Source == null || Document == null || Source.Revision != Document.Revision || Target.Revision != Document.Revision || Source.Id == Target.Id) return false;
            if (Target.Parent != null) return Source.Parent == Target.Parent;
            if (Region == 0) return !Source.IsGroup && Source.Parent != Target.Id;
            return true;
        }

        private void TileOver(object Sender, DragEventArgs E)
        {
            Button Tile = (Button)Sender;
            TileInfo Target = (TileInfo)Tile.Tag;
            int Region = DropRegion(Tile.PointToClient(new Point(E.X, E.Y)).X, Tile.Width);
            DragPayload Source = Payload(E.Data);
            ClearPreview();
            if (Source == null && E.Data.GetDataPresent(DataFormats.FileDrop) && Target.Revision == Document.Revision)
            {
                E.Effect = E.AllowedEffect & DragDropEffects.Copy;
                if (E.Effect == DragDropEffects.None) return;
                PreviewTile = Tile; PreviewRegion = Target.Parent == null ? Region : 0; Tile.Invalidate();
                SetStatus(Target.Parent != null || Region == 0 ? "Add dropped apps to this group" : "Insert dropped apps at this edge");
                return;
            }
            if (!CanDrop(Source, Target, Region)) { E.Effect = DragDropEffects.None; return; }
            PreviewTile = Tile; PreviewRegion = Target.Parent == null ? Region : Region == 0 ? 1 : Region; Tile.Invalidate();
            E.Effect = E.AllowedEffect & DragDropEffects.Move;
            SetStatus(Target.Parent != null ? "Reorder within group" : Region == 0 ? "Group with " + Tile.Text.Replace(Environment.NewLine, " ") :
                Source.Parent == null ? "Reorder at this edge" : "Move out of group at this edge");
        }

        private void TileDrop(object Sender, DragEventArgs E)
        {
            Button Tile = (Button)Sender;
            TileInfo Target = (TileInfo)Tile.Tag;
            int Region = DropRegion(Tile.PointToClient(new Point(E.X, E.Y)).X, Tile.Width);
            DragPayload Source = Payload(E.Data);
            ClearPreview();
            if (Source == null && E.Data.GetDataPresent(DataFormats.FileDrop) && Target.Revision == Document.Revision)
            {
                if ((E.AllowedEffect & DragDropEffects.Copy) == 0) return;
                string TargetId = Target.Parent ?? (Region == 0 ? Target.Id : null);
                ImportPaths(E.Data.GetData(DataFormats.FileDrop) as string[], TargetId,
                    TargetId == null ? Target.Index + (Region > 0 ? 1 : 0) : -1);
                return;
            }
            if ((E.AllowedEffect & DragDropEffects.Move) == 0 || !CanDrop(Source, Target, Region)) return;
            ApplyDrop(Source, Target, Region);
        }

        private void ApplyDrop(DragPayload Source, TileInfo Target, int Region)
        {
            ClearPreview();
            if (Target.Parent != null)
            {
                OrganizerGroup Group = Document.State.Groups.Single(Value => Value.Id == Target.Parent);
                int Current = Group.Members.IndexOf(Source.Id);
                int Position = Target.Index + (Region < 0 ? 0 : 1);
                if (Current < Position) Position--;
                Change(() => OrganizerStore.ReorderMember(Source.Revision, Source.Id, Position), "Member order saved.");
            }
            else if (Region == 0)
            {
                Change(() => OrganizerStore.GroupOnto(Source.Revision, Source.Id, Target.Id), "Group saved.");
                OrganizerGroup Group = Document.State.Groups.SingleOrDefault(Value => Value.Members.Contains(Source.Id));
                if (Group != null) { SelectedId = Group.Id; FocusedId = Group.Id; Render(); }
            }
            else MoveToBoundary(Source, Target.Index + (Region > 0 ? 1 : 0));
        }

        private void MoveToBoundary(DragPayload Source, int Boundary)
        {
            if (Source.Parent == null)
            {
                int Current = Document.State.Layout.FindIndex(Value => Value.Id == Source.Id);
                int Position = Boundary - (Current < Boundary ? 1 : 0);
                if (Position == Current) return;
                Change(() => OrganizerStore.Reorder(Source.Revision, Source.Id, Position), "Order saved.");
            }
            else
            {
                OrganizerGroup Parent = Document.State.Groups.Single(Value => Value.Id == Source.Parent);
                int ParentIndex = Document.State.Layout.FindIndex(Value => Value.Id == Parent.Id);
                // A one-member legacy group disappears; a two-member group leaves its survivor in place.
                int Position = Boundary - (Parent.Members.Count == 1 && ParentIndex < Boundary ? 1 : 0);
                Change(() => OrganizerStore.MoveOut(Source.Revision, Source.Id, Position), "App moved out; membership saved.");
            }
        }

        private void BackgroundOver(object Sender, DragEventArgs E)
        {
            ClearPreview();
            DragPayload Source = Payload(E.Data);
            E.Effect = Source != null && Source.Revision == Document.Revision ? DragDropEffects.Move :
                E.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            E.Effect &= E.AllowedEffect;
            SetStatus(Source == null ? "Add dropped apps to the organizer" : "Move to the end of the organizer");
        }
        private void BackgroundDrop(object Sender, DragEventArgs E)
        {
            DragPayload Source = Payload(E.Data);
            if (Source != null && Source.Revision == Document.Revision && (E.AllowedEffect & DragDropEffects.Move) != 0) MoveToBoundary(Source, Document.State.Layout.Count);
            else
            {
                string[] Files = E.Data.GetData(DataFormats.FileDrop) as string[];
                if (Files != null && (E.AllowedEffect & DragDropEffects.Copy) != 0) AddPaths(Files);
            }
        }
        private void MembersOver(object Sender, DragEventArgs E)
        {
            DragPayload Source = Payload(E.Data);
            bool Valid = Source != null && !Source.IsGroup && Source.Revision == Document.Revision &&
                Document.State.Groups.Any(Value => Value.Id == SelectedId && !Value.Deleted && Value.RedirectItemId == null);
            bool External = Source == null && E.Data.GetDataPresent(DataFormats.FileDrop) &&
                Document.State.Groups.Any(Value => Value.Id == SelectedId && !Value.Deleted && Value.RedirectItemId == null);
            E.Effect = (Valid ? DragDropEffects.Move : External ? DragDropEffects.Copy : DragDropEffects.None) & E.AllowedEffect;
            if (Valid || External) SetStatus(External ? "Add dropped apps to this group" : "Move app to the end of this group");
        }
        private void MembersDrop(object Sender, DragEventArgs E)
        {
            DragPayload Source = Payload(E.Data);
            if (Source == null && E.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if ((E.AllowedEffect & DragDropEffects.Copy) != 0 &&
                    Document.State.Groups.Any(Value => Value.Id == SelectedId && !Value.Deleted && Value.RedirectItemId == null))
                    ImportPaths(E.Data.GetData(DataFormats.FileDrop) as string[], SelectedId, -1);
                return;
            }
            if (Source == null || Source.IsGroup || Source.Revision != Document.Revision || (E.AllowedEffect & DragDropEffects.Move) == 0) return;
            OrganizerGroup Group = Document.State.Groups.SingleOrDefault(Value => Value.Id == SelectedId && !Value.Deleted && Value.RedirectItemId == null);
            if (Group == null) return;
            if (Source.Parent == Group.Id) Change(() => OrganizerStore.ReorderMember(Source.Revision, Source.Id, Group.Members.Count - 1), "Member order saved.");
            else Change(() => OrganizerStore.GroupOnto(Source.Revision, Source.Id, Group.Id), "App added to group.");
        }

        private void AddFiles(object Sender, EventArgs E)
        {
            using (OpenFileDialog Picker = new OpenFileDialog { Multiselect = true, DereferenceLinks = false, CheckFileExists = true,
                Filter = "Apps and shortcuts|*.exe;*.com;*.lnk;*.url", Title = "Add apps" })
                if (Picker.ShowDialog(this) == DialogResult.OK) AddPaths(Picker.FileNames);
        }

        internal void AddPaths(string[] Files)
        {
            if (Document != null) ImportPaths(Files, null, Document.State.Layout.Count);
        }

        private void ImportPaths(string[] Files, string TargetId, int Position)
        {
            if (Document == null || Files == null) return;
            int Invalid = 0;
            var Items = new List<ProgramShortcut>();
            foreach (string File in Files)
            {
                try
                {
                    ProgramShortcut Item = new ProgramShortcut { FilePath = LaunchService.AbsolutePath(File), WorkingDirectory = "" };
                    LaunchService.Build(Item);
                    Items.Add(Item);
                }
                catch (Exception Error) when (IconService.IsExpectedError(Error))
                { Invalid++; MainPath.Log(Error.Message, "Organizer"); }
            }
            ClearPreview();
            try
            {
                HashSet<string> ExistingGroups = new HashSet<string>(Document.State.Groups.Select(Value => Value.Id));
                OrganizerImportResult Result = OrganizerStore.ImportItems(Document.Revision, Items, TargetId, Position);
                Document = Result.Document;
                if (Result.GroupId != null) SelectedId = FocusedId = Result.GroupId;
                Render();
                SavedStatus("Added " + Result.Added + " app(s). " +
                    (Result.Skipped == 0 ? "" : Result.Skipped + " already imported item(s) skipped. ") +
                    (Invalid == 0 ? "" : Invalid + " unavailable item(s) skipped; see the log."));
                if (Result.GroupId != null && !ExistingGroups.Contains(Result.GroupId)) ShowPinGuide(this, EventArgs.Empty);
            }
            catch (Exception Error) when (IconService.IsExpectedError(Error))
            {
                try { Document = OrganizerStore.Load(); Render(); }
                catch (Exception ReadError) when (IconService.IsExpectedError(ReadError)) { MainPath.Log(ReadError.Message, "Organizer"); }
                ShowError(Error);
            }
        }

        private void OpenSelected(object Sender, EventArgs E)
        {
            if (Document == null) return;
            ProgramShortcut Item = Document.State.Items.SingleOrDefault(Value => Value.Id == (FocusedId ?? SelectedId));
            if (Item == null && Document.State.Layout.Any(Value => Value.Id == SelectedId && Value.IsGroup))
                Item = new ProgramShortcut { FilePath = MainPath.ExecutablePath, Arguments = SelectedId, WorkingDirectory = MainPath.InstallDirectory };
            if (Item == null) return;
            LaunchResult Result = LaunchService.Launch(Item);
            SetStatus(Result.Success ? "Opened." : "Could not open: " + Result.Error);
        }

        private void HidePinGuide()
        {
            PinGuide.Visible = false;
            PinRow.Height = 0;
            ResizeStrip();
            PinGroupId = PinRevision = null;
        }

        private void ShowPinGuide(object Sender, EventArgs E)
        {
            if (Document == null) return;
            try
            {
                GroupPublishing.GetShortcut(Document.Revision, SelectedId);
                OrganizerGroup Group = Document.State.Groups.Single(Value => Value.Id == SelectedId);
                PinGroupId = Group.Id;
                PinRevision = Document.Revision;
                PinInstructions.Text = "Pin “" + Group.Name + "” using Windows\nShow its shortcut, then right-click the selected file → Show more options → Pin to taskbar, if offered.";
                PinRow.Height = 104;
                ResizeStrip();
                PinGuide.Visible = true;
                SetStatus("Windows finishes pinning. This app does not track pin status.");
            }
            catch (Exception Error) when (IconService.IsExpectedError(Error)) { HidePinGuide(); ShowError(Error); }
        }

        private void OpenPinWindow(object Sender, EventArgs E)
        {
            if (PinGroupId == null) return;
            LaunchResult Result = GroupPublishing.OpenPinWindow(PinRevision, PinGroupId);
            SetStatus(Result.Success ? "Opened the group pin window. Pinning still requires your action there." : Result.Error);
        }

        private void ShowPinShortcut(object Sender, EventArgs E)
        {
            if (PinGroupId == null) return;
            LaunchResult Result = GroupPublishing.ShowShortcut(PinRevision, PinGroupId);
            SetStatus(Result.Success ? "Use Windows’ Pin to taskbar on the selected shortcut. Pin status is not tracked." :
                "Could not show shortcut: " + Result.Error);
        }

        private void BuildTileMenu(object Sender, System.ComponentModel.CancelEventArgs E)
        {
            Button Tile = TileMenu.SourceControl as Button;
            TileInfo Info = Tile == null ? null : Tile.Tag as TileInfo;
            if (Document == null || Info == null || Info.Revision != Document.Revision) { E.Cancel = true; return; }
            FocusedId = Info.Id;
            SelectedId = Info.Parent ?? Info.Id;
            TileMenu.Items.Clear();
            TileMenu.Items.Add("Open", null, delegate { OpenSelected(this, EventArgs.Empty); });
            if (Info.IsGroup)
            {
                TileMenu.Items.Add("Rename…", null, delegate { BeginInvoke(new Action(() => EditSelected(this, EventArgs.Empty))); });
                TileMenu.Items.Add("Choose icon…", null, delegate { BeginInvoke(new Action(ChooseGroupIcon)); });
                TileMenu.Items.Add("Pin to taskbar…", null, delegate { BeginInvoke(new Action(() => { Render(); ShowPinGuide(this, EventArgs.Empty); })); });
            }
            else TileMenu.Items.Add("Group with…", null, delegate { BeginInvoke(new Action(() => GroupFocused(this, EventArgs.Empty))); });
        }

        private void HideCustomization()
        {
            Customize.Visible = false;
            CustomizeRow.Height = 0;
            ResizeStrip();
            RenameId = RenameRevision = null;
        }

        private void EditSelected(object Sender, EventArgs E)
        {
            if (Document == null || !Document.State.Layout.Any(Value => Value.Id == SelectedId && Value.IsGroup)) return;
            Render();
            RenameId = SelectedId;
            RenameRevision = Document.Revision;
            NameEditor.Text = Document.State.Groups.Single(Value => Value.Id == RenameId).Name;
            CustomizeRow.Height = 42;
            ResizeStrip();
            Customize.Visible = true;
            NameEditor.Focus();
            NameEditor.SelectAll();
        }

        private void SaveRename(object Sender, EventArgs E)
        {
            if (RenameId == null) return;
            string Id = RenameId, Revision = RenameRevision, Name = NameEditor.Text;
            Change(() => OrganizerStore.RenameGroup(Revision, Id, Name), "Name saved.");
        }

        private void ChooseGroupIcon()
        {
            using (OpenFileDialog Picker = new OpenFileDialog { CheckFileExists = true,
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp", Title = "Choose group icon" })
                if (Picker.ShowDialog(this) == DialogResult.OK) ApplyGroupIcon(Picker.FileName);
        }

        private void ApplyGroupIcon(string PathName)
        {
            if (Document == null) return;
            Change(() => {
                Category Group = OrganizerStore.LoadGroup(SelectedId);
                if (Group == null) throw new InvalidDataException("Select an active group.");
                Group.OrganizerRevision = Document.Revision;
                using (Image Picture = Image.FromFile(PathName)) OrganizerStore.SaveGroup(Group, Picture);
                return OrganizerStore.Load();
            }, "Icon saved.");
        }

        private void GroupFocused(object Sender, EventArgs E)
        {
            if (Document == null || !Document.State.Items.Any(Value => Value.Id == FocusedId)) return;
            string Source = FocusedId;
            string Revision = Document.Revision;
            OrganizerGroup Parent = Document.State.Groups.SingleOrDefault(Value => Value.Members.Contains(Source));
            if (GroupMenu != null) GroupMenu.Dispose();
            ContextMenuStrip Menu = new ContextMenuStrip();
            GroupMenu = Menu;
            foreach (OrganizerEntry Target in Document.State.Layout.Where(Value => Value.Id != Source && (Parent == null || Value.Id != Parent.Id)))
            {
                string TargetId = Target.Id;
                string Name = Target.IsGroup ? Document.State.Groups.Single(Value => Value.Id == Target.Id).Name :
                    IconService.GetName(Document.State.Items.Single(Value => Value.Id == Target.Id));
                Menu.Items.Add(Name, null, delegate {
                    Change(() => OrganizerStore.GroupOnto(Revision, Source, TargetId), "Group saved.");
                    OrganizerGroup Group = Document.State.Groups.SingleOrDefault(Value => Value.Members.Contains(Source));
                    if (Group != null) { SelectedId = Group.Id; FocusedId = Group.Id; Render(); }
                });
            }
            if (Menu.Items.Count == 0) { Menu.Dispose(); SetStatus("Add another app to create a group."); return; }
            Menu.Closed += delegate {
                if (GroupMenu == Menu) GroupMenu = null;
                // ToolStrip finishes its own close processing after this event returns.
                if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(Menu.Dispose));
            };
            Menu.Show(LayoutGrid, new Point(8, 8));
        }

        private void MoveFocused(int Offset)
        {
            if (Document == null || FocusedId == null) return;
            OrganizerGroup Parent = Document.State.Groups.SingleOrDefault(Value => Value.Members.Contains(FocusedId));
            if (Parent != null)
            {
                int Position = Parent.Members.IndexOf(FocusedId) + Offset;
                if (Position >= 0 && Position < Parent.Members.Count)
                    Change(() => OrganizerStore.ReorderMember(Document.Revision, FocusedId, Position), "Member order saved.");
            }
            else
            {
                int Position = Document.State.Layout.FindIndex(Value => Value.Id == FocusedId) + Offset;
                if (Position >= 0 && Position < Document.State.Layout.Count)
                    Change(() => OrganizerStore.Reorder(Document.Revision, FocusedId, Position), "Order saved.");
            }
        }

        private void OnOrganizerKey(object Sender, KeyEventArgs E)
        {
            if (Customize.Visible && NameEditor.ContainsFocus)
            {
                if (E.KeyCode == Keys.Enter) { SaveRename(Sender, E); E.Handled = E.SuppressKeyPress = true; }
                else if (E.KeyCode == Keys.Escape) { HideCustomization(); E.Handled = E.SuppressKeyPress = true; }
                return;
            }
            if (E.Control && E.KeyCode == Keys.Z) { UndoButton.PerformClick(); E.Handled = true; }
            else if (E.Control && E.KeyCode == Keys.Y) { RedoButton.PerformClick(); E.Handled = true; }
            else if (E.Control && E.KeyCode == Keys.G) { GroupFocused(Sender, E); E.Handled = E.SuppressKeyPress = true; }
            else if (E.Alt && (E.KeyCode == Keys.Left || E.KeyCode == Keys.Right))
            {
                MoveFocused(E.KeyCode == Keys.Left ? -1 : 1);
                E.Handled = E.SuppressKeyPress = true;
            }
            else if (E.Control && E.Shift && E.KeyCode == Keys.M)
            {
                if (Document != null)
                {
                    OrganizerGroup Parent = Document.State.Groups.SingleOrDefault(Value => Value.Members.Contains(FocusedId));
                    if (Parent != null)
                        MoveToBoundary(new DragPayload { Id = FocusedId, Parent = Parent.Id, Revision = Document.Revision, Session = Session }, Document.State.Layout.Count);
                }
                E.Handled = E.SuppressKeyPress = true;
            }
            else if (E.KeyCode == Keys.F5) { ReloadDocument(); E.Handled = true; }
            else if (E.KeyCode == Keys.Enter) { OpenSelected(Sender, E); E.Handled = true; }
            else if (E.KeyCode == Keys.Escape) { ClearPreview(); SetStatus("Drag cancelled."); E.Handled = true; }
        }
    }
}
