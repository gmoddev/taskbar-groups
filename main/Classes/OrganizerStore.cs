using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace client.Classes
{
    // Activated by the organizer surface; popup startup only reads existing authority.
    // Once activated, this pointer is authoritative for ALL group readers and writers.
    internal static class OrganizerStore
    {
        private const int HistoryLimit = 50;
        internal static Action<string> Checkpoint = null;
        private static string Root { get { return Path.Combine(MainPath.DataDirectory, "Organizer"); } }
        private static string CurrentPath { get { return Path.Combine(Root, "Current.xml"); } }
        private static string PreviousPath { get { return Path.Combine(Root, "Previous.xml"); } }
        public static bool IsActive { get { return PointerExists(CurrentPath) || PointerExists(PreviousPath); } }
        private static void Step(string Name) { if (Checkpoint != null) Checkpoint(Name); }

        private static bool PointerExists(string PathName)
        {
            try { File.GetAttributes(PathName); return true; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            // Inaccessible organizer data must never reactivate stale per-group pointers.
        }

        private static string SnapshotPath(string Revision)
        {
            MainPath.RejectReparsePoint(Root);
            string Versions = Path.Combine(Root, "Versions");
            MainPath.RejectReparsePoint(Versions);
            return Path.Combine(Versions, GroupStore.Token(Revision) + ".xml");
        }

        private static OrganizerDocument ReadSnapshot(string Revision)
        {
            OrganizerDocument Document = GroupStore.Read<OrganizerDocument>(SnapshotPath(Revision));
            if (Document.SchemaVersion != 1) throw new NotSupportedException("Upgrade Taskbar Groups to read this organizer version.");
            if (Document.Revision != Revision || Document.Undo == null || Document.Redo == null || Document.PinnedSources == null ||
                Document.Undo.Count > HistoryLimit || Document.Redo.Count > HistoryLimit)
                throw new InvalidDataException("Invalid organizer history.");
            foreach (string Id in Document.Undo.Concat(Document.Redo)) GroupStore.Token(Id);
            OrganizerModel.Validate(Document.State);
            ValidateResources(Document.State);
            return Document;
        }

        private static OrganizerDocument ReadPointer(string PathName)
        {
            GroupPointer Pointer = GroupStore.Read<GroupPointer>(PathName);
            if (Pointer.SchemaVersion != 1) throw new NotSupportedException("Upgrade Taskbar Groups to read this organizer pointer.");
            if (Pointer.Deleted) throw new InvalidDataException("Organizer pointer cannot be a tombstone.");
            return ReadSnapshot(Pointer.Revision);
        }

        public static OrganizerDocument Load()
        {
            try { return ReadPointer(CurrentPath); }
            catch (Exception Error) when (!(Error is NotSupportedException) && (GroupStore.IsDataError(Error) || Error is OutOfMemoryException))
            {
                OrganizerDocument Previous = ReadPointer(PreviousPath);
                Previous.Recovered = true;
                MainPath.Warn("Recovered previous complete organizer snapshot. " + Error.Message);
                return Previous;
            }
        }

        private static bool SameItem(ProgramShortcut Left, ProgramShortcut Right)
        {
            return Left.Id == Right.Id && Left.FilePath == Right.FilePath && Left.Arguments == Right.Arguments &&
                Left.WorkingDirectory == Right.WorkingDirectory && Left.isWindowsApp == Right.isWindowsApp && Left.name == Right.name;
        }

        private static List<ProgramShortcut> Members(OrganizerState State, OrganizerGroup Group)
        {
            IEnumerable<string> Ids = Group.RedirectItemId == null ? (IEnumerable<string>)Group.Members : new[] { Group.RedirectItemId };
            return Ids.Select(Id => State.Items.Single(Item => Item.Id == Id)).ToList();
        }

        private static void ValidateResources(OrganizerState State)
        {
            foreach (OrganizerGroup Group in State.Groups.Where(Value => !Value.Deleted))
            {
                Category Saved = GroupStore.ReadGeneration(Group.StoreKey, Group.Revision);
                List<ProgramShortcut> Expected = Members(State, Group);
                if (Saved.Id != Group.Id || Saved.Name != Group.Name || Saved.ShortcutList.Count != Expected.Count ||
                    !Saved.ShortcutList.Zip(Expected, SameItem).All(Equal => Equal))
                    throw new InvalidDataException("Organizer membership does not match its saved group resources.");
            }
        }

        private static Category ReadGroup(OrganizerDocument Document, OrganizerGroup Group)
        {
            if (Group == null || Group.Deleted) return null;
            Category Result = GroupStore.ReadGeneration(Group.StoreKey, Group.Revision);
            Result.OrganizerRevision = Document.Revision;
            return Result;
        }

        public static Category LoadGroup(string Identity)
        {
            OrganizerDocument Document = Load();
            OrganizerGroup Group = Document.State.Groups.SingleOrDefault(Value =>
                string.Equals(Value.Id, Identity, StringComparison.OrdinalIgnoreCase) || string.Equals(Value.StoreKey, Identity, StringComparison.OrdinalIgnoreCase));
            return ReadGroup(Document, Group);
        }

        public static IEnumerable<Category> LoadGroups()
        {
            OrganizerDocument Document = Load();
            return Document.State.Layout.Where(Entry => Entry.IsGroup)
                .Select(Entry => ReadGroup(Document, Document.State.Groups.Single(Group => Group.Id == Entry.Id))).ToList();
        }

        public static OrganizerDocument Initialize()
        {
            using (FileStream Lock = GroupStore.GetLock())
            {
                if (IsActive) return Load();
                MainPath.GetFolder(Root);
                MainPath.GetFolder(Path.Combine(Root, "Versions"));
                OrganizerDocument First = new OrganizerDocument();
                foreach (Category Existing in GroupStore.LoadUnmanagedGroups().OrderBy(Group => Group.StoreKey, StringComparer.OrdinalIgnoreCase))
                {
                    Category Saved = Existing;
                    if (Saved.SchemaVersion != 2)
                        using (Bitmap Picture = Existing.LoadIconImage()) Saved = GroupStore.Stage(Existing, Picture);
                    First.State.Items.AddRange(OrganizerModel.Copy(Saved.ShortcutList));
                    First.State.Groups.Add(new OrganizerGroup { Id = Saved.Id, Name = Saved.Name, StoreKey = Saved.StoreKey,
                        Revision = Saved.Revision, Members = Saved.ShortcutList.Select(Item => Item.Id).ToList() });
                    First.State.Layout.Add(new OrganizerEntry { Id = Saved.Id, IsGroup = true });
                    Step("ImportedGroup");
                }
                return Publish(null, First, "Initialize organizer");
            }
        }

        private static OrganizerDocument RequireRevision(string Revision)
        {
            OrganizerDocument Current = Load();
            if (Current.Revision != Revision) throw new IOException("The organizer changed in another window. Reload before trying again.");
            return Current;
        }

        private static Bitmap Composite(Category Group)
        {
            Bitmap Result = new Bitmap(256, 256);
            try
            {
                using (Graphics Canvas = Graphics.FromImage(Result))
                {
                    Canvas.Clear(Color.FromArgb(31, 31, 31));
                    for (int Index = 0; Index < Math.Min(4, Group.ShortcutList.Count); Index++)
                        using (Image Picture = Group.loadImageCache(Group.ShortcutList[Index]))
                            Canvas.DrawImage(Picture, new Rectangle(8 + Index % 2 * 128, 8 + Index / 2 * 128, 112, 112));
                }
                return Result;
            }
            catch { Result.Dispose(); throw; }
        }

        private static void StageGroups(OrganizerState State, Category Override = null, Image OverridePicture = null)
        {
            int Staged = 0;
            foreach (OrganizerGroup Record in State.Groups.Where(Group => !Group.Deleted))
            {
                List<ProgramShortcut> Expected = Members(State, Record);
                Category Saved = Record.Revision == null ? null : GroupStore.ReadGeneration(Record.StoreKey, Record.Revision);
                bool Custom = Override != null && Override.Id == Record.Id;
                bool Unchanged = Saved != null && Saved.Name == Record.Name && Saved.ShortcutList.Count == Expected.Count &&
                    Saved.ShortcutList.Zip(Expected, SameItem).All(Equal => Equal);
                if (Unchanged && !Custom) continue;
                Category Draft = Custom ? GroupStore.Copy(Override) : Saved == null ? new Category { Width = Math.Min(4, Expected.Count) } : GroupStore.Copy(Saved);
                Draft.Id = Record.Id;
                Draft.StoreKey = Record.StoreKey;
                Draft.AppIdKey = Saved == null ? Record.Id : Saved.AppIdKey;
                Draft.Name = Record.Name;
                Draft.ShortcutList = OrganizerModel.Copy(Expected);
                // Preserve chosen artwork; regenerate automatic composites after membership changes.
                using (Image Picture = Custom ? new Bitmap(OverridePicture) : Saved == null || Record.AutoIcon ? Composite(Draft) : Saved.LoadIconImage())
                {
                    Category Next = GroupStore.Stage(Draft, Picture);
                    Record.Revision = Next.Revision;
                }
                Step("GroupStaged:" + ++Staged);
            }
        }

        private static void Push(List<string> Stack, string Revision)
        {
            Stack.Add(Revision);
            if (Stack.Count > HistoryLimit) Stack.RemoveAt(0);
        }

        private static OrganizerDocument Publish(OrganizerDocument Current, OrganizerDocument Next, string Operation)
        {
            OrganizerModel.Validate(Next.State);
            ValidateResources(Next.State);
            Next.SchemaVersion = 1;
            Next.Revision = Guid.NewGuid().ToString("N");
            Next.Operation = Operation;
            Next.Recovered = false;
            GroupStore.Write(SnapshotPath(Next.Revision), Next);
            Step("SnapshotFlushed");
            ReadSnapshot(Next.Revision);
            Step("SnapshotValidated");
            string Pending = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".pending");
            GroupStore.Write(Pending, new GroupPointer { Revision = Next.Revision });
            Step("PointerFlushed");
            if (File.Exists(CurrentPath))
            {
                MainPath.RejectReparsePoint(CurrentPath);
                string Backup = Current != null && Current.Recovered ? Path.Combine(Root, "Damaged.xml") : PreviousPath;
                if (File.Exists(Backup)) MainPath.RejectReparsePoint(Backup);
                File.Replace(Pending, CurrentPath, Backup);
            }
            else File.Move(Pending, CurrentPath);
            Step("Committed");
            RepairLinks(Next);
            return Next;
        }

        public static void RepairLinks(OrganizerDocument Document)
        {
            foreach (OrganizerGroup Group in Document.State.Groups.Where(Value => !Value.Deleted))
            {
                try { GroupStore.RepairLink(ReadGroup(Document, Group)); }
                catch (Exception Error) when (GroupStore.IsDataError(Error)) { MainPath.Warn("Launcher repair deferred: " + Error.Message); }
            }
        }

        private static OrganizerDocument Commit(OrganizerDocument Current, OrganizerState State, string Operation, Category Override = null, Image Picture = null)
        {
            OrganizerModel.Validate(State);
            StageGroups(State, Override, Picture);
            OrganizerDocument Next = new OrganizerDocument { State = State, Undo = new List<string>(Current.Undo), PinnedSources = new List<string>(Current.PinnedSources) };
            Push(Next.Undo, Current.Revision);
            return Publish(Current, Next, Operation);
        }

        private static OrganizerDocument Change(string Revision, string Operation, Action<OrganizerState> Transform)
        {
            using (FileStream Lock = GroupStore.GetLock())
            {
                OrganizerDocument Current = RequireRevision(Revision);
                OrganizerState State = OrganizerModel.Copy(Current.State);
                Transform(State);
                return Commit(Current, State, Operation);
            }
        }

        public static OrganizerDocument AddItem(string Revision, ProgramShortcut Item, int Position)
        {
            if (Item == null) throw new ArgumentNullException(nameof(Item));
            return Change(Revision, "Add item", State =>
            {
                if (Position < 0 || Position > State.Layout.Count) throw new InvalidDataException("Invalid item position.");
                ProgramShortcut Added = OrganizerModel.Copy(Item);
                Added.Id = Added.Id ?? Guid.NewGuid().ToString("N");
                State.Items.Add(Added);
                State.Layout.Insert(Position, new OrganizerEntry { Id = Added.Id });
            });
        }

        // One external drop is one commit/history entry, including creation of its group.
        public static OrganizerImportResult ImportItems(string Revision, List<ProgramShortcut> Items, string TargetId, int Position)
        { return ImportBatch(Revision, Items, TargetId, Position, null); }

        public static OrganizerImportResult ImportBatch(string Revision, List<ProgramShortcut> Items, string TargetId, int Position, List<string> PinnedSources)
        {
            if (Items == null) throw new ArgumentNullException(nameof(Items));
            using (FileStream Lock = GroupStore.GetLock())
            {
                OrganizerDocument Current = RequireRevision(Revision);
                OrganizerState State = OrganizerModel.Copy(Current.State);
                if (TargetId == null)
                {
                    if (Position < 0 || Position > State.Layout.Count) throw new InvalidDataException("Invalid import position.");
                }
                else if (Position != -1 || !State.Layout.Any(Value => Value.Id == TargetId))
                    throw new InvalidDataException("Import target is no longer available.");
                OrganizerImportResult Result = new OrganizerImportResult { Document = Current };
                string GroupTarget = TargetId;
                foreach (ProgramShortcut Item in Items)
                {
                    if (Item == null) throw new InvalidDataException("Missing import item.");
                    ProgramShortcut Added = OrganizerModel.Copy(Item);
                    LaunchService.Build(Added); // Validate without dispatching or resolving shell UI.
                    if (State.Items.Any(Value => string.Equals(Value.FilePath, Added.FilePath, StringComparison.OrdinalIgnoreCase) &&
                        (Value.Arguments ?? "") == (Added.Arguments ?? "") &&
                        string.Equals(Value.WorkingDirectory ?? "", Added.WorkingDirectory ?? "", StringComparison.OrdinalIgnoreCase) &&
                        Value.isWindowsApp == Added.isWindowsApp)) { Result.Skipped++; continue; }
                    Added.Id = Guid.NewGuid().ToString("N");
                    State.Items.Add(Added);
                    if (GroupTarget == null) State.Layout.Insert(Position++, new OrganizerEntry { Id = Added.Id });
                    else
                    {
                        State.Layout.Add(new OrganizerEntry { Id = Added.Id });
                        GroupTarget = OrganizerModel.GroupOnto(State, Added.Id, GroupTarget);
                        Result.GroupId = GroupTarget;
                    }
                    Result.Added++;
                }
                bool NewReceipts = PinnedSources != null && PinnedSources.Any(Value => !Current.PinnedSources.Contains(Value, StringComparer.OrdinalIgnoreCase));
                if (NewReceipts) Current.PinnedSources = Current.PinnedSources.Concat(PinnedSources).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (Result.Added != 0) Result.Document = Commit(Current, State, "Import apps");
                else if (NewReceipts) Result.Document = Publish(Current, OrganizerModel.Copy(Current), "Remember pinned imports");
                return Result;
            }
        }

        public static OrganizerDocument RenameGroup(string Revision, string GroupId, string Name)
        {
            return Change(Revision, "Rename group", State => {
                OrganizerGroup Group = State.Groups.SingleOrDefault(Value => Value.Id == GroupId && !Value.Deleted && Value.RedirectItemId == null);
                if (Group == null) throw new InvalidDataException("Select an active group.");
                Group.Name = (Name ?? "").Trim();
            });
        }

        public static OrganizerDocument GroupOnto(string Revision, string SourceId, string TargetId)
        { return Change(Revision, "Group item", State => OrganizerModel.GroupOnto(State, SourceId, TargetId)); }

        public static OrganizerDocument MoveOut(string Revision, string ItemId, int Position)
        { return Change(Revision, "Move item out", State => OrganizerModel.MoveOut(State, ItemId, Position)); }

        public static OrganizerDocument Reorder(string Revision, string EntryId, int Position)
        { return Change(Revision, "Reorder organizer", State => OrganizerModel.Reorder(State, EntryId, Position)); }

        public static OrganizerDocument ReorderMember(string Revision, string ItemId, int Position)
        { return Change(Revision, "Reorder member", State => OrganizerModel.ReorderMember(State, ItemId, Position)); }

        public static OrganizerDocument Undo(string Revision) { return Restore(Revision, false); }
        public static OrganizerDocument Redo(string Revision) { return Restore(Revision, true); }

        private static OrganizerDocument Restore(string Revision, bool Redo)
        {
            using (FileStream Lock = GroupStore.GetLock())
            {
                OrganizerDocument Current = RequireRevision(Revision);
                List<string> Source = Redo ? Current.Redo : Current.Undo;
                if (Source.Count == 0) throw new InvalidOperationException(Redo ? "Nothing to redo." : "Nothing to undo.");
                OrganizerDocument Target = ReadSnapshot(Source.Last());
                OrganizerDocument Next = new OrganizerDocument { State = Target.State, Undo = new List<string>(Current.Undo), Redo = new List<string>(Current.Redo), PinnedSources = new List<string>(Current.PinnedSources) };
                List<string> Pop = Redo ? Next.Redo : Next.Undo;
                Pop.RemoveAt(Pop.Count - 1);
                Push(Redo ? Next.Undo : Next.Redo, Current.Revision);
                // Always publish a fresh revision, including undo, to reject stale editors (ABA).
                return Publish(Current, Next, Redo ? "Redo" : "Undo");
            }
        }

        public static void SaveGroup(Category Group, Image Picture)
        {
            using (FileStream Lock = GroupStore.GetLock())
            {
                OrganizerDocument Current = Group.StoreKey == null ? Load() : RequireRevision(Group.OrganizerRevision);
                OrganizerState State = OrganizerModel.Copy(Current.State);
                Category Draft = GroupStore.Copy(Group);
                Draft.Id = Draft.Id ?? Guid.NewGuid().ToString("N");
                OrganizerGroup Record = State.Groups.SingleOrDefault(Value => Value.Id == Draft.Id);
                if (Record == null)
                {
                    if (Group.StoreKey != null) throw new IOException("Group was removed.");
                    Record = new OrganizerGroup { Id = Draft.Id, StoreKey = Draft.Id, Name = Draft.Name };
                    State.Groups.Add(Record);
                    State.Layout.Add(new OrganizerEntry { Id = Record.Id, IsGroup = true });
                }
                else if (Record.Deleted || Record.RedirectItemId != null || Record.Revision != Group.Revision || Record.StoreKey != Group.StoreKey)
                    throw new IOException("This group changed. Reopen the editor.");
                List<string> OldMembers = new List<string>(Record.Members);
                Record.Members.Clear();
                Record.Name = Draft.Name;
                Record.AutoIcon = false;
                foreach (ProgramShortcut Item in Draft.ShortcutList)
                {
                    Item.Id = Item.Id ?? Guid.NewGuid().ToString("N");
                    ProgramShortcut Existing = State.Items.SingleOrDefault(Value => Value.Id == Item.Id);
                    if (Existing != null && !OldMembers.Contains(Item.Id)) throw new InvalidDataException("Member belongs to another organizer position.");
                    if (Existing != null) State.Items.Remove(Existing);
                    State.Items.Add(OrganizerModel.Copy(Item));
                    Record.Members.Add(Item.Id);
                }
                int Position = State.Layout.FindIndex(Entry => Entry.Id == Record.Id) + 1;
                foreach (string Removed in OldMembers.Except(Record.Members)) State.Layout.Insert(Position++, new OrganizerEntry { Id = Removed });
                OrganizerDocument Next = Commit(Current, State, "Edit group", Draft, Picture);
                Category Saved = ReadGroup(Next, Next.State.Groups.Single(Value => Value.Id == Draft.Id));
                GroupStore.Adopt(Group, Saved);
            }
        }

        public static void DeleteGroup(Category Group)
        {
            Change(Group.OrganizerRevision, "Delete group", State =>
            {
                OrganizerGroup Record = State.Groups.SingleOrDefault(Value => Value.Id == Group.Id);
                if (Record == null || Record.Deleted || Record.RedirectItemId != null || Record.Revision != Group.Revision)
                    throw new IOException("This group changed. Reopen the editor.");
                int Position = State.Layout.FindIndex(Entry => Entry.Id == Record.Id);
                State.Layout.RemoveAt(Position);
                foreach (string Member in Record.Members) State.Layout.Insert(Position++, new OrganizerEntry { Id = Member });
                Record.Members.Clear();
                Record.Deleted = true;
            });
        }
    }
}
