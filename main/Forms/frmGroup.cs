using ChinhDo.Transactions;
using client.Classes;
using client.User_controls;
using IWshRuntimeLibrary;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.WindowsAPICodePack.Shell;
using Microsoft.WindowsAPICodePack.Dialogs; 

namespace client.Forms
{
    public partial class frmGroup : Form
    {
        public Category Category;
        public frmClient Client;
        public bool IsNew;
        private String[] imageExt = new String[] { ".png", ".jpg", ".jpe", ".jfif", ".jpeg", };
        private String[] extensionExt = new String[] { ".exe", ".lnk", ".url" };
        private String[] specialImageExt = new String[] { ".ico", ".exe", ".lnk" };

        public ucProgramShortcut selectedShortcut;

        private Image OwnedGroupIcon;

        private List<ProgramShortcut> shortcutChanged = new List<ProgramShortcut>();


        //--------------------------------------
        // CTOR AND LOAD
        //--------------------------------------

        // CTOR for creating a new group
        public frmGroup(frmClient client)
        {
            // Setting from profile
            System.Runtime.ProfileOptimization.StartProfile("frmGroup.Profile");

            InitializeComponent();
            Disposed += delegate { if (OwnedGroupIcon != null) OwnedGroupIcon.Dispose(); };

            // Setting default category properties
            Category = new Category { ShortcutList = new List<ProgramShortcut>() };
            Client = client;
            IsNew = true;

            // Setting default control values
            cmdDelete.Visible = false;
            cmdSave.Left += 70;
            cmdExit.Left += 70;
            radioDark.Checked = true;
        }

        // CTOR for editing an existing group
        public frmGroup(frmClient client, Category category)
        {
            // Setting form profile
            System.Runtime.ProfileOptimization.StartProfile("frmGroup.Profile");

            InitializeComponent();

            Disposed += delegate { if (OwnedGroupIcon != null) { OwnedGroupIcon.Dispose(); OwnedGroupIcon = null; } };
            // Setting properties
            Category = GroupStore.Copy(category);
            Client = client;
            IsNew = false;

            // Setting control values from loaded group
            this.Text = "Edit group";
            txtGroupName.Text = Category.SchemaVersion == 2 ? Category.Name : Regex.Replace(Category.Name, @"(_)+", " ");
            pnlAllowOpenAll.Checked = category.allowOpenAll;
            SetGroupIcon(Category.LoadIconImage());
            lblNum.Text = Category.Width.ToString();
            lblOpacity.Text = Category.Opacity.ToString();
           
            if (Category.ColorString == null)  // Handles if groups is created from earlier releas w/o ColorString property
                Category.ColorString = System.Drawing.ColorTranslator.ToHtml(Color.FromArgb(31, 31, 31));

            Color categoryColor = ImageFunctions.FromString(Category.ColorString);
            
            if (categoryColor == Color.FromArgb(31, 31, 31))
                radioDark.Checked = true;
            else if (categoryColor == Color.FromArgb(230, 230, 230))
                radioLight.Checked = true;
            else
            {
                radioCustom.Checked = true;
                pnlCustomColor.Visible = true;
                pnlCustomColor.BackColor = categoryColor;
            }

            // Loading existing shortcutpanels
            int position = 0;
            foreach (ProgramShortcut psc in Category.ShortcutList)
            {
                LoadShortcut(psc, position);
                position++;
            }
        }

        // Handle scaling etc(?) (WORK IN PROGRESS)
        private void frmGroup_Load(object sender, EventArgs e)
        {
            // Scaling form (WORK IN PROGRESS)
            this.MaximumSize = new Size(605, Screen.PrimaryScreen.WorkingArea.Height);
        }

        //--------------------------------------
        // SHORTCUT PANEL HANLDERS
        //--------------------------------------

        // Load up shortcut panel
        public void LoadShortcut(ProgramShortcut psc, int position)
        {
            pnlShortcuts.AutoScroll = false;
            ucProgramShortcut ucPsc = new ucProgramShortcut()
            {
                MotherForm = this,
                Shortcut = psc,
                Position = position,
            };
            pnlShortcuts.Controls.Add(ucPsc);
            ucPsc.Show();
            ucPsc.BringToFront();

            if (pnlShortcuts.Controls.Count < 6)
            {
                pnlShortcuts.Height += 50;
                pnlAddShortcut.Top += 50;
            }
            ucPsc.Location = new Point(25, (pnlShortcuts.Controls.Count * 50)-50);
            pnlShortcuts.AutoScroll = true;

        }

        // Adding shortcut by button
        private void pnlAddShortcut_Click(object sender, EventArgs e)
        {
            resetSelection();

            lblErrorShortcut.Visible = false; // resetting error msg

            if (Category.ShortcutList.Count >= 20)
            {
                lblErrorShortcut.Text = "Max 20 shortcuts in one group";
                lblErrorShortcut.BringToFront();
                lblErrorShortcut.Visible = true;
            }


            OpenFileDialog openFileDialog = new OpenFileDialog // ask user to select exe file
            {
                InitialDirectory = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs",
                Title = "Create New Shortcut",
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = true,
                DefaultExt = "exe",
                Filter = "Exe or Shortcut (.exe, .lnk)|*.exe;*.lnk;*.url",
                RestoreDirectory = true,
                ReadOnlyChecked = true,
                DereferenceLinks = false
             };

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                foreach (String file in openFileDialog.FileNames)
                {
                    addShortcut(file);
                }
                resetSelection();
            }

            if (pnlShortcuts.Controls.Count != 0)
            {
                pnlShortcuts.ScrollControlIntoView(pnlShortcuts.Controls[0]);
            }
        }

        // Handle dropped programs into the add program/shortcut field
        private void pnlDragDropExt(object sender, DragEventArgs e)
        {
            try
            {
                string[] Files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (Files != null)
                {
                    foreach (string File in Files) addShortcut(File);
                }
                else
                {
                    using (ShellObjectCollection Items = ShellObjectCollection.FromDataObject((System.Runtime.InteropServices.ComTypes.IDataObject)e.Data))
                        foreach (ShellObject Item in Items) addShortcut(Item.ParsingName, true);
                }
                resetSelection();
            }
            catch (Exception Error) when (IconService.IsExpectedError(Error) || Error is InvalidCastException)
            { ShowItemError(Error.Message); }
        }

        // Handle adding the shortcut to list
        private void addShortcut(String file, bool isExtension = false)
        {
            try
            {
                ProgramShortcut Item = new ProgramShortcut { FilePath = Environment.ExpandEnvironmentVariables(file),
                    isWindowsApp = isExtension, WorkingDirectory = "" };
                LaunchPlan Plan = LaunchService.Build(Item); // Validate only; adding never launches.
                Item.WorkingDirectory = Plan.WorkingDirectory;
                Category.ShortcutList.Add(Item);
                LoadShortcut(Item, Category.ShortcutList.Count - 1);
            }
            catch (Exception Error) when (LaunchService.IsExpectedError(Error)) { ShowItemError(Error.Message); }
        }

        private void ShowItemError(string Message)
        {
            MainPath.Log(Message, "Editor");
            lblErrorShortcut.Text = Message;
            lblErrorShortcut.Visible = true;
            lblErrorShortcut.BringToFront();
        }

        public void DeleteShortcut(ProgramShortcut Item)
        {
            resetSelection();
            Category.ShortcutList.Remove(Item);
            ReloadShortcuts();
        }

        private void ReloadShortcuts()
        {
            while (pnlShortcuts.Controls.Count > 0) pnlShortcuts.Controls[0].Dispose();
            pnlShortcuts.Height = 0;
            pnlAddShortcut.Top = 220;
            selectedShortcut = null;
            for (int Index = 0; Index < Category.ShortcutList.Count; Index++)
                LoadShortcut(Category.ShortcutList[Index], Index);
        }

        // Change positions of shortcut panels
        public void Swap<T>(IList<T> list, int indexA, int indexB)
        {
            resetSelection();
            if (indexA < 0 || indexB < 0 || indexA >= list.Count || indexB >= list.Count) return;
            T tmp = list[indexA];
            list[indexA] = list[indexB];
            list[indexB] = tmp;

            ReloadShortcuts();
        }



        //--------------------------------------
        // IMAGE HANDLERS
        //--------------------------------------

        // Adding icon by button
        private void cmdAddGroupIcon_Click(object sender, EventArgs e)
        {
            resetSelection();

            lblErrorIcon.Visible = false;  //resetting error msg

            OpenFileDialog openFileDialog = new OpenFileDialog  // ask user to select img as group icon
            {
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Title = "Select Group Icon",
                CheckFileExists = true,
                CheckPathExists = true,
                DefaultExt = "img",
                Filter = "Image files and exec (*.jpg, *.jpeg, *.jpe, *.jfif, *.png, *.exe, *.ico) | *.jpg; *.jpeg; *.jpe; *.jfif; *.png; *.ico; *.exe",
                FilterIndex = 2,
                RestoreDirectory = true,
                ReadOnlyChecked = true,
                DereferenceLinks = false,
        };

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {

                String imageExtension = Path.GetExtension(openFileDialog.FileName).ToLower();

                handleIcon(openFileDialog.FileName, imageExtension);
            }
        }

        // Handle drag and dropped images
        private void pnlDragDropImg(object sender, DragEventArgs e)
        {
            resetSelection();

            try
            {
                string[] Files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (Files == null || Files.Length != 1) return;
                string Extension = Path.GetExtension(Files[0]).ToLowerInvariant();
                if (imageExt.Concat(specialImageExt).Contains(Extension)) handleIcon(Files[0], Extension);
            }
            catch (Exception Error) when (IconService.IsExpectedError(Error)) { ShowItemError(Error.Message); }
        }

        private void handleIcon(String file, String imageExtension)
        {
            try
            {
                Bitmap Picture;
                if (specialImageExt.Contains(imageExtension))
                {
                    bool Success;
                    Picture = IconService.GetIcon(new ProgramShortcut { FilePath = file }, out Success);
                    if (!Success) { Picture.Dispose(); throw new InvalidDataException("This file has no usable icon."); }
                }
                else
                {
                    using (Image Source = Image.FromFile(file)) Picture = new Bitmap(Source);
                }
                SetGroupIcon(Picture);
                lblAddGroupIcon.Text = "Change group icon";
                lblErrorIcon.Visible = false;
            }
            catch (Exception Error) when (IconService.IsExpectedError(Error))
            {
                MainPath.Log(Error.Message, "Editor");
                lblErrorIcon.Text = "Could not read icon; the previous icon is unchanged.";
                lblErrorIcon.Visible = true;
            }
        }

        private void SetGroupIcon(Image Picture)
        {
            Image Previous = OwnedGroupIcon;
            OwnedGroupIcon = Picture;
            cmdAddGroupIcon.BackgroundImage = Picture;
            if (Previous != null) Previous.Dispose();
        }

        public static Bitmap handleLnkExt(string File)
        {
            return IconService.GetIcon(new ProgramShortcut { FilePath = File });
        }

        public static string handleExtName(string File)
        {
            return Path.GetFileNameWithoutExtension(File);
        }

        // Below two functions highlights the background as you would if you hovered over it with a mosue
        // Use checkExtension to allow file dropping after a series of checks
        // Only highlights if the files being dropped are valid in extension wise
        private void pnlDragDropEnterExt(object sender, DragEventArgs e)
        {
            resetSelection();

            if (checkExtensions(e, extensionExt))
            {
                pnlAddShortcut.BackColor = Color.FromArgb(23, 23, 23);
            }
        }

        private void pnlDragDropEnterImg(object sender, DragEventArgs e)
        {
            resetSelection();

            if (checkExtensions(e, imageExt.Concat(specialImageExt).ToArray()))
            {
                pnlGroupIcon.BackColor = Color.FromArgb(23, 23, 23);
            }
        }

        // Series of checks to make sure it can be dropped
        private Boolean checkExtensions(DragEventArgs e, String[] exts)
        {

            try
            {
                if (e.Data.GetDataPresent("Shell IDList Array")) { e.Effect = e.AllowedEffect; return true; }
                string[] Files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (Files == null || Files.Length == 0) return false;
                foreach (string File in Files)
                    if (!exts.Contains(Path.GetExtension(File).ToLowerInvariant()) && !Directory.Exists(File)) return false;
                e.Effect = DragDropEffects.Copy;
                return true;
            }
            catch (Exception Error) when (IconService.IsExpectedError(Error))
            { ShowItemError(Error.Message); return false; }
        }

        //--------------------------------------
        // SAVE/EXIT/DELETE GROUP
        //--------------------------------------

        // Exit editor
        private void cmdExit_Click(object sender, EventArgs e)
        {
            this.Hide();
            this.Dispose();
            Client.Reload(); //flush and reload category panels
        }

        // Save group
        private void cmdSave_Click(object sender, EventArgs e)
        {
            resetSelection();

            //List <Directory> directories = 

            if (txtGroupName.Text == "Name the new group...") // Verify category name
            {
                lblErrorTitle.Text = "Must select a name";
                lblErrorTitle.Visible = true;
            }
            else if (string.IsNullOrWhiteSpace(txtGroupName.Text) || txtGroupName.Text.Length > 200)
            {
                lblErrorTitle.Text = "Choose a display name of 1 to 200 characters";
                lblErrorTitle.Visible = true;
            }
            else if (cmdAddGroupIcon.BackgroundImage ==
                global::client.Properties.Resources.AddWhite) // Verify icon
            {
                lblErrorIcon.Text = "Must select group icon";
                lblErrorIcon.Visible = true;
            }
            else if (Category.ShortcutList.Count == 0) // Verify shortcuts
            {
                lblErrorShortcut.Text = "Must select at least one shortcut";
                lblErrorShortcut.Visible = true;
            }
            else
            {
                try
                {

                    foreach (ProgramShortcut Item in shortcutChanged)
                        if (Category.ShortcutList.Contains(Item) && !string.IsNullOrWhiteSpace(Item.WorkingDirectory) &&
                            !Directory.Exists(LaunchService.AbsolutePath(Item.WorkingDirectory)))
                            throw new InvalidDataException("The configured working directory is unavailable.");

                    Category.Width = int.Parse(lblNum.Text);
                    Category.Name = txtGroupName.Text.Trim();
                    Category.CreateConfig(cmdAddGroupIcon.BackgroundImage);

                    this.Dispose();
                    Client.Reload();
                }
                catch (Exception Error) when (GroupStore.IsDataError(Error))
                {
                    ShowStorageError(Error);
                }

                Client.Reset();
            }

        }

        // Delete group
        private void cmdDelete_Click(object sender, EventArgs e)
        {
            resetSelection();

            try
            {
                GroupStore.Delete(Category);
                Dispose();
                Client.Reload();
            }
            catch (Exception Error) when (GroupStore.IsDataError(Error))
            {
                ShowStorageError(Error);
            }
            Client.Reset();
        }

        private void ShowStorageError(Exception Error)
        {
            MainPath.Log("Editor operation failed: " + Error.Message);
            lblErrorTitle.Text = Error.Message;
            lblErrorTitle.Visible = true;
        }

        //--------------------------------------
        // UI CUSTOMIZATION
        //--------------------------------------

        // Change category width
        private void cmdWidthUp_Click(object sender, EventArgs e)
        {
            resetSelection();

            int num = int.Parse(lblNum.Text);
            if (num > 19)
            {
                lblErrorNum.Text = "Max width";
                lblErrorNum.Visible = true;
            }
            else
            {
                num++;
                lblErrorNum.Visible = false;
                lblNum.Text = num.ToString();
            }
        }
        private void cmdWidthDown_Click(object sender, EventArgs e)
        {
            resetSelection();

            int num = int.Parse(lblNum.Text);
            if (num == 1)
            {
                lblErrorNum.Text = "Width cant be less than 1";
                lblErrorNum.Visible = true;
            }
            else
            {
                num--;
                lblErrorNum.Visible = false;
                lblNum.Text = num.ToString();
            }
        }

        // Color radio buttons
        private void radioCustom_Click(object sender, EventArgs e)
        {
            if (colorDialog.ShowDialog() == DialogResult.OK)
            {
                Category.ColorString = System.Drawing.ColorTranslator.ToHtml(colorDialog.Color);
                pnlCustomColor.Visible = true;
                pnlCustomColor.BackColor = colorDialog.Color;
            }
        }

        private void radioDark_Click(object sender, EventArgs e)
        {
            Category.ColorString = System.Drawing.ColorTranslator.ToHtml(Color.FromArgb(31, 31, 31));
            pnlCustomColor.Visible = false;
        }

        private void radioLight_Click(object sender, EventArgs e)
        {
            Category.ColorString = System.Drawing.ColorTranslator.ToHtml(Color.FromArgb(230, 230, 230));
            pnlCustomColor.Visible = false;
        }

        // Opacity buttons
        private void numOpacUp_Click(object sender, EventArgs e)
        {
            double op = double.Parse(lblOpacity.Text);
            op += 10;
            Category.Opacity = op;
            lblOpacity.Text = op.ToString();
            numOpacDown.Enabled = true;
            numOpacDown.BackgroundImage = global::client.Properties.Resources.NumDownWhite;

            if (op > 90)
            {
                numOpacUp.Enabled = false;
                numOpacUp.BackgroundImage = global::client.Properties.Resources.NumUpGray;
            }
        }

        private void numOpacDown_Click(object sender, EventArgs e)
        {
            double op = double.Parse(lblOpacity.Text);
            op -= 10;
            Category.Opacity = op;
            lblOpacity.Text = op.ToString();
            numOpacUp.Enabled = true;
            numOpacUp.BackgroundImage = global::client.Properties.Resources.NumUpWhite;

            if (op < 10)
            {
                numOpacDown.Enabled = false;
                numOpacDown.BackgroundImage = global::client.Properties.Resources.NumDownGray;
            }
        }

        //--------------------------------------
        // FORM VISUAL INTERACTIONS
        //--------------------------------------

        private void pnlGroupIcon_MouseEnter(object sender, EventArgs e)
        {
            pnlGroupIcon.BackColor = Color.FromArgb(23, 23, 23);
        }

        private void pnlGroupIcon_MouseLeave(object sender, EventArgs e)
        {
            pnlGroupIcon.BackColor = Color.FromArgb(31, 31, 31);
        }

        private void pnlAddShortcut_MouseEnter(object sender, EventArgs e)
        {
            pnlAddShortcut.BackColor = Color.FromArgb(23, 23, 23);
        }

        private void pnlAddShortcut_MouseLeave(object sender, EventArgs e)
        {
            pnlAddShortcut.BackColor = Color.FromArgb(31, 31, 31);
        }

        // Handles placeholder text for group name
        private void txtGroupName_MouseClick(object sender, MouseEventArgs e)
        {
            resetSelection();
            if (txtGroupName.Text == "Name the new group...")
                txtGroupName.Text = "";
        }

        private void txtGroupName_Leave(object sender, EventArgs e)
        {
            if (txtGroupName.Text == "")
                txtGroupName.Text = "Name the new group...";
        }

        // Error labels
        private void txtGroupName_TextChanged(object sender, EventArgs e)
        {
            lblErrorTitle.Visible = false;
        }

        //--------------------------------------
        // SHORTCUT/PRGORAM SELECTION
        //--------------------------------------

        // Deselect selected program/shortcut
        public void resetSelection()
        {
            pnlArgumentTextbox.Enabled = false;
            cmdSelectDirectory.Enabled = false;
            if (selectedShortcut != null)
            {
                pnlColor.Visible = true;
                pnlArguments.Visible = false;
                selectedShortcut.ucDeselected();
                selectedShortcut.IsSelected = false;
                selectedShortcut = null;
            }
        }

        // Enable the argument textbox once a shortcut/program has been selected
        public void enableSelection(ucProgramShortcut passedShortcut)
        {
            selectedShortcut = passedShortcut;
            passedShortcut.ucSelected();
            passedShortcut.IsSelected = true;

            pnlArgumentTextbox.Text = Category.ShortcutList[selectedShortcut.Position].Arguments;
            pnlArgumentTextbox.Enabled = true;

            pnlWorkingDirectory.Text = Category.ShortcutList[selectedShortcut.Position].WorkingDirectory;
            pnlWorkingDirectory.Enabled = true;
            cmdSelectDirectory.Enabled = true;

            pnlColor.Visible = false;
            pnlArguments.Visible = true;
        }

        // Set the argument property to whatever the user set
        private void pnlArgumentTextbox_TextChanged(object sender, EventArgs e)
        {
            Category.ShortcutList[selectedShortcut.Position].Arguments = pnlArgumentTextbox.Text;
        }

        // Clear textbox focus
        private void pnlArgumentTextbox_KeyDown(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                lblAddGroupIcon.Focus();


                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        // Manage the checkbox allowing opening all shortcuts
        private void pnlAllowOpenAll_CheckedChanged(object sender, EventArgs e)
        {
            Category.allowOpenAll = pnlAllowOpenAll.Checked;
        }

        private void cmdSelectDirectory_Click(object sender, EventArgs e)
        {
            CommonOpenFileDialog openFileDialog = new CommonOpenFileDialog()
            {
                EnsurePathExists = true,
                IsFolderPicker = true,
                InitialDirectory = Category.ShortcutList[selectedShortcut.Position].WorkingDirectory
            };

            if (openFileDialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                this.Focus();
                Category.ShortcutList[selectedShortcut.Position].WorkingDirectory = openFileDialog.FileName;
            }
        }

        private void pnlWorkingDirectory_TextChanged(object sender, EventArgs e)
        {
            Category.ShortcutList[selectedShortcut.Position].WorkingDirectory = pnlWorkingDirectory.Text;

            if (!shortcutChanged.Contains(Category.ShortcutList[selectedShortcut.Position]))
            {
                shortcutChanged.Add(Category.ShortcutList[selectedShortcut.Position]);
            }
        }

        private void frmGroup_MouseClick(object sender, MouseEventArgs e)
        {
            resetSelection();
        }
    }
}
