using System;
using System.Drawing;
using System.Windows.Forms;
using client.Classes;
using client.Forms;
using System.IO;
using System.Text.RegularExpressions;

namespace client.User_controls
{
    public partial class ucCategoryPanel : UserControl
    {
        public Category Category;
        public frmClient Client;
        public ucCategoryPanel(frmClient client, Category category)
        {
            InitializeComponent();
            Client = client;
            Category = category;
            lblTitle.Text = category.SchemaVersion == 2 ? category.Name : Regex.Replace(category.Name, @"(_)+", " ");
            picGroupIcon.BackgroundImage = Category.LoadIconImage();
            Disposed += delegate { picGroupIcon.BackgroundImage.Dispose(); };

            // starting values for position of shortcuts
            int x = 90;
            int y = 55;
            int columns = 1;

            foreach (ProgramShortcut psc in Category.ShortcutList) // since this is calculating uc height it cant be placed in load
            {
                if (columns == 8)
                {
                    x = 90; // resetting x
                    y += 40; // adding new row
                    this.Height += 40;
                    columns = 1;
                }
                CreateShortcut(x, y, psc);
                x += 50;
                columns++;
            }
        }

        private void CreateShortcut(int x, int y, ProgramShortcut programShortcut)
        {
            // creating shortcut picturebox from shortcut
            this.shortcutPanel = new System.Windows.Forms.PictureBox
            {
                BackColor = System.Drawing.Color.Transparent,
                Location = new System.Drawing.Point(x, y),
                Size = new System.Drawing.Size(30, 30),
                BackgroundImageLayout = ImageLayout.Stretch,
                TabStop = false
            };
            this.shortcutPanel.MouseEnter += new System.EventHandler((sender, e) => Client.EnterControl(sender, e, this));
            this.shortcutPanel.MouseLeave += new System.EventHandler((sender, e) => Client.LeaveControl(sender, e, this));
            this.shortcutPanel.Click += new System.EventHandler((sender, e) => OpenFolder(sender, e));

            PictureBox Picture = shortcutPanel;
            Picture.BackgroundImage = Category.loadImageCache(programShortcut);
            Picture.Disposed += delegate { if (Picture.BackgroundImage != null) Picture.BackgroundImage.Dispose(); };

            this.Controls.Add(this.shortcutPanel);
            this.shortcutPanel.Show();
            this.shortcutPanel.BringToFront();
        }

        private void ucNewCategory_Load(object sender, EventArgs e)
        {
            cmdDelete.Top = (this.Height / 2) - (cmdDelete.Height / 2);

        }

        public void OpenFolder(object sender, EventArgs e)
        {
            // Open the shortcut folder for the group when click on category panel

            // Build path based on the directory of the main .exe file
            string filePath = Category.SchemaVersion == 2 ? GroupStore.GetLink(Category) : MainPath.GetShortcutPath(Category.Name);

            // Open directory in explorer and highlighting file
            LaunchResult Result = LaunchService.Launch(new ProgramShortcut {
                FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                Arguments = string.Format("/select,\"{0}\"", filePath), WorkingDirectory = "" });
            if (!Result.Success) Client.ShowError(Result.Error);
        }

        private void cmdDelete_Click(object sender, EventArgs e)
        {
            frmGroup editGroup = new frmGroup(Client, Category);
            editGroup.Show();
            editGroup.BringToFront();
        }

        public static Bitmap LoadBitmap(string path) // needed to access img without occupying read/write
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                var memoryStream = new MemoryStream(reader.ReadBytes((int)stream.Length));
                reader.Close();
                stream.Close();
                return new Bitmap(memoryStream);
            }
        }

        private void lblTitle_MouseEnter(object sender, EventArgs e)
        {
            Client.EnterControl(sender, e, this);

        }

        private void lblTitle_MouseLeave(object sender, EventArgs e)
        {
            Client.LeaveControl(sender, e, this);
        }

        //
        // endregion
        //
        public System.Windows.Forms.PictureBox shortcutPanel;

    }
}
