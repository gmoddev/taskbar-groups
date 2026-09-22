namespace client.Classes
{
    public class ProgramShortcut
    {
        public string Id { get; set; }
        public string FilePath { get; set; }
        public bool isWindowsApp { get; set; }

        public string name { get; set; } = "";
        public string Arguments = "";
        public string WorkingDirectory = MainPath.InstallDirectory;
 

        public ProgramShortcut() // needed for XML serialization
        {

        }

    }
}
