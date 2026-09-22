using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace client.Classes
{
    public class OrganizerEntry
    {
        public string Id;
        public bool IsGroup;
    }

    public class OrganizerGroup
    {
        public string Id;
        public string StoreKey;
        public string Revision;
        public string Name;
        public List<string> Members = new List<string>();
        // Hidden launcher identities remain valid when a group dissolves.
        public string RedirectItemId;
        public bool Deleted;
        public bool AutoIcon;
    }

    public class OrganizerState
    {
        public List<OrganizerEntry> Layout = new List<OrganizerEntry>();
        public List<ProgramShortcut> Items = new List<ProgramShortcut>();
        public List<OrganizerGroup> Groups = new List<OrganizerGroup>();
    }

    public class OrganizerDocument
    {
        public int SchemaVersion = 1;
        public string Revision;
        public string Operation;
        public OrganizerState State = new OrganizerState();
        public List<string> Undo = new List<string>();
        public List<string> Redo = new List<string>();
        // Import receipts are not undone: restarting must not resurrect an undone import.
        public List<string> PinnedSources = new List<string>();
        [XmlIgnore] public bool Recovered;
    }

    public sealed class OrganizerImportResult
    {
        public OrganizerDocument Document;
        public int Added, Skipped;
        public string GroupId;
    }

    // Pure transformations on caller-owned copies. No controls, files or process launching.
    internal static class OrganizerModel
    {
        public static T Copy<T>(T Value)
        {
            using (MemoryStream Buffer = new MemoryStream())
            {
                XmlSerializer Serializer = new XmlSerializer(typeof(T));
                Serializer.Serialize(Buffer, Value);
                Buffer.Position = 0;
                return (T)Serializer.Deserialize(Buffer);
            }
        }

        public static void Validate(OrganizerState State)
        {
            if (State == null || State.Layout == null || State.Items == null || State.Groups == null)
                throw new InvalidDataException("Incomplete organizer state.");
            HashSet<string> Ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> Owned = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> Keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProgramShortcut Item in State.Items)
            {
                if (Item == null || string.IsNullOrWhiteSpace(Item.FilePath) || !Ids.Add(GroupStore.Token(Item.Id)))
                    throw new InvalidDataException("Invalid or duplicate organizer item.");
            }
            HashSet<string> ItemIds = new HashSet<string>(Ids, StringComparer.Ordinal);
            foreach (OrganizerGroup Group in State.Groups)
            {
                if (Group == null || !Ids.Add(GroupStore.Token(Group.Id)) || !MainPath.IsValidGroupName(Group.StoreKey) ||
                    !Keys.Add(Group.StoreKey) || Group.Members == null || string.IsNullOrWhiteSpace(Group.Name) || Group.Name.Length > 200)
                    throw new InvalidDataException("Invalid organizer group.");
                if (Group.RedirectItemId != null && (!ItemIds.Contains(Group.RedirectItemId) || Group.Members.Count != 0 || Group.Deleted))
                    throw new InvalidDataException("Invalid dissolved-group redirect.");
                if (Group.Deleted && Group.Members.Count != 0) throw new InvalidDataException("Deleted group owns members.");
                if (!Group.Deleted && Group.RedirectItemId == null && Group.Members.Count == 0)
                    throw new InvalidDataException("Empty active group.");
                foreach (string Member in Group.Members)
                    if (!ItemIds.Contains(Member) || !Owned.Add(Member)) throw new InvalidDataException("Missing or multiply-owned member.");
            }
            HashSet<string> VisibleGroups = new HashSet<string>();
            foreach (OrganizerEntry Entry in State.Layout)
            {
                if (Entry == null) throw new InvalidDataException("Missing layout entry.");
                if (Entry.IsGroup)
                {
                    OrganizerGroup Group = State.Groups.SingleOrDefault(Value => Value.Id == Entry.Id);
                    if (Group == null || Group.Deleted || Group.RedirectItemId != null || !VisibleGroups.Add(Entry.Id))
                        throw new InvalidDataException("Invalid group position.");
                }
                else if (!ItemIds.Contains(Entry.Id) || !Owned.Add(Entry.Id)) throw new InvalidDataException("Invalid standalone position.");
            }
            if (Owned.Count != ItemIds.Count || State.Groups.Any(Group => !Group.Deleted && Group.RedirectItemId == null && !VisibleGroups.Contains(Group.Id)))
                throw new InvalidDataException("Organizer contains unplaced items or groups.");
            if (State.Groups.Any(Group => State.Groups.Any(Other => Other.Id != Group.Id && string.Equals(Other.Id, Group.StoreKey, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("Legacy alias conflicts with another group identity.");
        }

        private static OrganizerGroup Parent(OrganizerState State, string ItemId)
        {
            if (!State.Items.Any(Item => Item.Id == ItemId)) throw new InvalidDataException("Item was not found.");
            return State.Groups.SingleOrDefault(Group => Group.Members.Contains(ItemId));
        }

        private static void Normalize(OrganizerState State, OrganizerGroup Group)
        {
            if (Group == null || Group.Members.Count > 1) return;
            int Position = State.Layout.FindIndex(Entry => Entry.IsGroup && Entry.Id == Group.Id);
            if (Position < 0) throw new InvalidDataException("Missing group position.");
            State.Layout.RemoveAt(Position);
            if (Group.Members.Count == 1)
            {
                Group.RedirectItemId = Group.Members[0];
                State.Layout.Insert(Position, new OrganizerEntry { Id = Group.RedirectItemId });
            }
            else Group.Deleted = true;
            Group.Members.Clear();
        }

        private static void Detach(OrganizerState State, string ItemId)
        {
            OrganizerGroup Owner = Parent(State, ItemId);
            if (Owner == null) State.Layout.RemoveAll(Entry => !Entry.IsGroup && Entry.Id == ItemId);
            else { Owner.Members.Remove(ItemId); Normalize(State, Owner); }
        }

        public static string GroupOnto(OrganizerState State, string SourceId, string TargetId)
        {
            if (SourceId == TargetId) throw new InvalidDataException("Cannot drop an item onto itself.");
            OrganizerGroup Source = Parent(State, SourceId);
            OrganizerEntry Target = State.Layout.SingleOrDefault(Entry => Entry.Id == TargetId);
            if (Target == null) throw new InvalidDataException("Drop target must be a top-level item or group.");
            if (Target.IsGroup)
            {
                OrganizerGroup Group = State.Groups.Single(Value => Value.Id == TargetId);
                if (Group == Source) throw new InvalidDataException("Item is already in that group.");
                Detach(State, SourceId);
                Group.Members.Add(SourceId);
                return Group.Id;
            }
            Detach(State, SourceId);
            int Position = State.Layout.FindIndex(Entry => Entry.Id == TargetId);
            string Id = Guid.NewGuid().ToString("N");
            int Number = 1;
            while (State.Groups.Any(Group => Group.Name == "Group " + Number)) Number++;
            OrganizerGroup Created = new OrganizerGroup { Id = Id, StoreKey = Id, Name = "Group " + Number, AutoIcon = true,
                Members = new List<string> { TargetId, SourceId } };
            State.Groups.Add(Created);
            State.Layout[Position] = new OrganizerEntry { Id = Id, IsGroup = true };
            return Id;
        }

        // Position is an insertion index in the layout AFTER the item is detached.
        public static void MoveOut(OrganizerState State, string ItemId, int Position)
        {
            if (Parent(State, ItemId) == null) throw new InvalidDataException("Item is already standalone.");
            Detach(State, ItemId);
            if (Position < 0 || Position > State.Layout.Count) throw new InvalidDataException("Invalid drop position.");
            State.Layout.Insert(Position, new OrganizerEntry { Id = ItemId });
        }

        public static void Reorder(OrganizerState State, string EntryId, int Position)
        {
            OrganizerEntry Entry = State.Layout.SingleOrDefault(Value => Value.Id == EntryId);
            if (Entry == null || Position < 0 || Position >= State.Layout.Count) throw new InvalidDataException("Invalid reorder.");
            State.Layout.Remove(Entry);
            State.Layout.Insert(Position, Entry);
        }

        public static void ReorderMember(OrganizerState State, string ItemId, int Position)
        {
            OrganizerGroup Group = Parent(State, ItemId);
            if (Group == null || Position < 0 || Position >= Group.Members.Count) throw new InvalidDataException("Invalid member position.");
            Group.Members.Remove(ItemId);
            Group.Members.Insert(Position, ItemId);
        }
    }
}
