// ImportGraphics but it can also set sprite properties and import more types of files.

// Based off of ImportGraphics.csx by the UTMT team
// and ImportGraphicsWithParameters.csx by @DonavinDraws
// ImportGraphicsAdvanced-specific edits (extra formats and animation speed) made by CST1229

// Texture packer by Samuel Roy
// Uses code from https://github.com/mfascia/TexturePacker

// revision 2: fixed gif import not working unless the folder was named Sprites,
// fixed the default origin being Top Center instead of Top Left and
// reworded Is special type?'s boolean and the background import error message
// revision 3: added optional support for single-frame sprites if a frame number is not specified
// revision 4: added support for the texture handling refactor
// revision 5: handle breaking Magick.NET changes, disabled animation speed options in GMS1 games, hi-DPI support,
// renamed from ImportGraphicsWithParametersPlus to ImportGraphicsAdvanced
// revision 6: sprite texture items are now cropped, to save on texture page space and to fix sprite fonts

using ImageMagick;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using UndertaleModLib.Models;
using UndertaleModLib.Util;
using UndertaleModLib;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime;

EnsureDataLoaded();

static bool importAsSprite = true;
static bool importFrameless = false;

string[] offsets = { "Top Left", "Top Center", "Top Right", "Center Left", "Center", "Center Right", "Bottom Left", "Bottom Center", "Bottom Right" };

string[] playbacks = { "Frames Per Second", "Frames Per Game Frame" };

static List<MagickImage> imagesToCleanup = new();

float animSpd = 1;

bool isSpecial = false;
uint specialVer = 1;

string offresult;

int playback;

HashSet<string> spritesStartAt1 = new HashSet<string>();

string importFolder = CheckValidity();

string packDir = Path.Combine(ExePath, "Packager");
Directory.CreateDirectory(packDir);

bool noMasksForBasicRectangles = Data.IsVersionAtLeast(2022, 9); // TODO: figure out the exact version, but this is pretty close

try
{
    string sourcePath = importFolder;
    string outName = Path.Combine(packDir, "atlas.txt");
    int textureSize = 2048;
    int PaddingValue = 2;
    bool debug = false;
    Packer packer = new Packer();
    packer.Process(sourcePath, textureSize, PaddingValue, debug);
    packer.SaveAtlasses(outName);

    int lastTextPage = Data.EmbeddedTextures.Count - 1;
    int lastTextPageItem = Data.TexturePageItems.Count - 1;

    bool bboxMasks = Data.IsVersionAtLeast(2024, 6);
    Dictionary<UndertaleSprite, Node> maskNodes = new();

    // Import everything into UTMT
    string prefix = outName.Replace(Path.GetExtension(outName), "");
    int atlasCount = 0;
    //OffsetResult();
    foreach (Atlas atlas in packer.Atlasses)
    {
        string atlasName = Path.Combine(packDir, $"{prefix}{atlasCount:000}.png");
        using MagickImage atlasImage = TextureWorker.ReadBGRAImageFromFile(atlasName);
        IPixelCollection<byte> atlasPixels = atlasImage.GetPixels();

        UndertaleEmbeddedTexture texture = new();
        texture.Name = new UndertaleString($"Texture {++lastTextPage}");
        texture.TextureData.Image = GMImage.FromMagickImage(atlasImage).ConvertToPng(); // TODO: other formats?
        Data.EmbeddedTextures.Add(texture);
        foreach (Node n in atlas.Nodes)
        {
            if (n.Texture != null)
            {
                // Initalize values of this texture
                UndertaleTexturePageItem texturePageItem = new UndertaleTexturePageItem();
                texturePageItem.Name = new UndertaleString("PageItem " + ++lastTextPageItem);
                texturePageItem.SourceX = (ushort)n.Bounds.X;
                texturePageItem.SourceY = (ushort)n.Bounds.Y;
                texturePageItem.SourceWidth = (ushort)n.Bounds.Width;
                texturePageItem.SourceHeight = (ushort)n.Bounds.Height;
                texturePageItem.TargetX = (ushort)n.Texture.TargetX;
                texturePageItem.TargetY = (ushort)n.Texture.TargetY;
                texturePageItem.TargetWidth = (ushort)n.Bounds.Width;
                texturePageItem.TargetHeight = (ushort)n.Bounds.Height;
                texturePageItem.BoundingWidth = (ushort)n.Texture.BoundingWidth;
                texturePageItem.BoundingHeight = (ushort)n.Texture.BoundingHeight;
                texturePageItem.TexturePage = texture;

                // Add this texture to UMT
                Data.TexturePageItems.Add(texturePageItem);

                // String processing
                string stripped = Path.GetFileNameWithoutExtension(n.Texture.Source);
            }
        }
        // Increment atlas
        atlasCount++;
    }

    HideProgressBar();
    //ScriptMessage("Import Complete!");
}
finally
{
    foreach (MagickImage img in imagesToCleanup)
    {
        img.Dispose();
    }
}

public class TextureInfo
{
    public string Source;
    public int Width;
    public int Height;
    public int TargetX;
    public int TargetY;
    public int BoundingWidth;
    public int BoundingHeight;
    public MagickImage Image;
}

public enum SpriteType
{
    Sprite,
    Background,
    Font,
    Unknown
}


public enum SplitType
{
    Horizontal,
    Vertical,
}

public enum BestFitHeuristic
{
    Area,
    MaxOneAxis,
}

public struct Rect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public class Node
{
    public Rect Bounds;
    public TextureInfo Texture;
    public SplitType SplitType;
}

public class Atlas
{
    public int Width;
    public int Height;
    public List<Node> Nodes;
}

public class Packer
{
    public List<TextureInfo> SourceTextures;
    public StringWriter Log;
    public StringWriter Error;
    public int Padding;
    public int AtlasSize;
    public bool DebugMode;
    public BestFitHeuristic FitHeuristic;
    public List<Atlas> Atlasses;
    public HashSet<string> Sources;

    public Packer()
    {
        SourceTextures = new List<TextureInfo>();
        Log = new StringWriter();
        Error = new StringWriter();
    }

    public void Process(string _SourceDir, int _AtlasSize, int _Padding, bool _DebugMode)
    {
        Padding = _Padding;
        AtlasSize = _AtlasSize;
        DebugMode = _DebugMode;
        //1: scan for all the textures we need to pack
        Sources = new HashSet<string>();
        ScanForTextures(_SourceDir);
        List<TextureInfo> textures = new List<TextureInfo>();
        textures = SourceTextures.ToList();
        //2: generate as many atlasses as needed (with the latest one as small as possible)
        Atlasses = new List<Atlas>();
        while (textures.Count > 0)
        {
            Atlas atlas = new Atlas();
            atlas.Width = _AtlasSize;
            atlas.Height = _AtlasSize;
            List<TextureInfo> leftovers = LayoutAtlas(textures, atlas);
            if (leftovers.Count == 0)
            {
                // we reached the last atlas. Check if this last atlas could have been twice smaller
                while (leftovers.Count == 0)
                {
                    atlas.Width /= 2;
                    atlas.Height /= 2;
                    leftovers = LayoutAtlas(textures, atlas);
                }
                // we need to go 1 step larger as we found the first size that is too small
                // if the atlas is 0x0 then it should be 1x1 instead
                if (atlas.Width == 0)
                {
                    atlas.Width = 1;
                } else
                {
                    atlas.Width *= 2;
                }
                if (atlas.Height == 0)
                {
                    atlas.Height = 1;
                }
                else
                {
                    atlas.Height *= 2;
                }
                leftovers = LayoutAtlas(textures, atlas);
            }
            Atlasses.Add(atlas);
            textures = leftovers;
        }
    }

    public void SaveAtlasses(string _Destination)
    {
        int atlasCount = 0;
        string prefix = _Destination.Replace(Path.GetExtension(_Destination), "");
        string descFile = _Destination;
        StreamWriter tw = new StreamWriter(_Destination);
        tw.WriteLine("source_tex, atlas_tex, x, y, width, height");
        foreach (Atlas atlas in Atlasses)
        {
            string atlasName = $"{prefix}{atlasCount:000}.png";

            // 1: Save images
            using (MagickImage img = CreateAtlasImage(atlas))
                TextureWorker.SaveImageToFile(img, atlasName);

            // 2: save description in file
            foreach (Node n in atlas.Nodes)
            {
                if (n.Texture != null)
                {
                    tw.Write(n.Texture.Source + ", ");
                    tw.Write(atlasName + ", ");
                    tw.Write((n.Bounds.X).ToString() + ", ");
                    tw.Write((n.Bounds.Y).ToString() + ", ");
                    tw.Write((n.Bounds.Width).ToString() + ", ");
                    tw.WriteLine((n.Bounds.Height).ToString());
                }
            }
            ++atlasCount;
        }
        tw.Close();
        tw = new StreamWriter(prefix + ".log");
        tw.WriteLine("--- LOG -------------------------------------------");
        tw.WriteLine(Log.ToString());
        tw.WriteLine("--- ERROR -----------------------------------------");
        tw.WriteLine(Error.ToString());
        tw.Close();
    }

    private void ScanForTextures(string _Path)
    {
        //DirectoryInfo di = new DirectoryInfo(_Path);
        //FileInfo[] files = di.GetFiles("*", SearchOption.AllDirectories);

        FileInfo[] files = new FileInfo[1];
        files[0] = new FileInfo(_Path);

        foreach (FileInfo fi in files)
        {
            SpriteType spriteType = GetSpriteType(fi.FullName);
            string ext = Path.GetExtension(fi.FullName);

            bool isSprite = spriteType == SpriteType.Sprite || (spriteType == SpriteType.Unknown && importAsSprite);

            if (ext == ".gif")
            {
                // animated .gif
                string dirName = Path.GetDirectoryName(fi.FullName);
                string spriteName = Path.GetFileNameWithoutExtension(fi.FullName);

                MagickReadSettings settings = new()
                {
                    ColorSpace = ColorSpace.sRGB,
                };
                using MagickImageCollection gif = new(fi.FullName, settings);
                int frames = gif.Count;
                if (!isSprite && frames > 1)
                {
                    throw new ScriptException(fi.FullName + " is a " + spriteType + ", but has more than 1 frame. Script has been stopped.");
                }

                for (int i = frames - 1; i >= 0; i--)
                {
                    AddSource(
                        (MagickImage)gif[i],
                        Path.Join(
                            dirName,
                            isSprite ?
                                (spriteName + "_" + i + ".png") : (spriteName + ".png")
                        )
                    );
                    // don't auto-dispose
                    gif.RemoveAt(i);
                }
            }
            else if (ext == ".png")
            {
                Match stripMatch = null;
                if (isSprite)
                {
                    stripMatch = Regex.Match(Path.GetFileNameWithoutExtension(fi.Name), @"(.*)_strip(\d+)");
                }
                if (stripMatch is not null && stripMatch.Success)
                {
                    string spriteName = stripMatch.Groups[1].Value;
                    string frameCountStr = stripMatch.Groups[2].Value;

                    uint frames;
                    try
                    {
                        frames = UInt32.Parse(frameCountStr);
                    }
                    catch
                    {
                        throw new ScriptException(fi.FullName + " has an invalid strip numbering scheme. Script has been stopped.");
                    }
                    if (frames <= 0)
                    {
                        throw new ScriptException(fi.FullName + " has 0 frames. Script has been stopped.");
                    }

                    if (!isSprite && frames > 1)
                    {
                        throw new ScriptException(fi.FullName + " is not a sprite, but has more than 1 frame. Script has been stopped.");
                    }

                    MagickReadSettings settings = new()
                    {
                        ColorSpace = ColorSpace.sRGB,
                    };
                    using MagickImage img = new(fi.FullName, settings);
                    if ((img.Width % frames) > 0)
                    {
                        throw new ScriptException(fi.FullName + " has a width not divisible by the number of frames. Script has been stopped.");
                    }

                    string dirName = Path.GetDirectoryName(fi.FullName);

                    uint frameWidth = (uint)img.Width / frames;
                    uint frameHeight = (uint)img.Height;
                    for (uint i = 0; i < frames; i++)
                    {
                        AddSource(
                            (MagickImage)img.Clone(
                                (int)(frameWidth * i), 0, frameWidth, frameHeight
                            ),
                            Path.Join(dirName,
                                isSprite ?
                                    (spriteName + "_" + i + ".png") : (spriteName + ".png")
                            )
                        );
                    }
                }
                else
                {
                    MagickReadSettings settings = new()
                    {
                        ColorSpace = ColorSpace.sRGB,
                    };
                    MagickImage img = new(fi.FullName);
                    AddSource(img, fi.FullName);
                }
            }
        }
    }

    private void AddSource(MagickImage img, string fullName)
    {
        imagesToCleanup.Add(img);
        if (img.Width <= AtlasSize && img.Height <= AtlasSize)
        {
            TextureInfo ti = new TextureInfo();

            if (!Sources.Add(fullName))
            {
                throw new ScriptException(
                    Path.GetFileNameWithoutExtension(fullName) +
                    " as a frame already exists (possibly due to having multiple types of sprite images named the same). Script has been stopped."
                );
            }

            ti.Source = fullName;
            ti.BoundingWidth = (int)img.Width;
            ti.BoundingHeight = (int)img.Height;

            // GameMaker doesn't trim tilesets. I assume it didn't trim backgrounds too
            ti.TargetX = 0;
            ti.TargetY = 0;
            if (GetSpriteType(ti.Source) != SpriteType.Background)
            {
                img.BorderColor = MagickColors.Transparent;
                img.BackgroundColor = MagickColors.Transparent;
                img.Border(1);
                IMagickGeometry? bbox = img.BoundingBox;
                if (bbox is not null)
                {
                    ti.TargetX = bbox.X - 1;
                    ti.TargetY = bbox.Y - 1;
                    // yes, .Trim() mutates the image...
                    // it doesn't really matter though since it isn't written back or anything
                    img.Trim();
                }
                else
                {
                    // Empty sprites should be 1x1
                    ti.TargetX = 0;
                    ti.TargetY = 0;
                    img.Crop(1, 1);
                }
                img.ResetPage();
            }
            ti.Width = (int)img.Width;
            ti.Height = (int)img.Height;
            ti.Image = img;

            SourceTextures.Add(ti);

            Log.WriteLine("Added " + fullName);
        }
        else
        {
            Error.WriteLine(fullName + " is too large to fix in the atlas. Skipping!");
        }
    }

    private void HorizontalSplit(Node _ToSplit, int _Width, int _Height, List<Node> _List)
    {
        Node n1 = new Node();
        n1.Bounds.X = _ToSplit.Bounds.X + _Width + Padding;
        n1.Bounds.Y = _ToSplit.Bounds.Y;
        n1.Bounds.Width = _ToSplit.Bounds.Width - _Width - Padding;
        n1.Bounds.Height = _Height;
        n1.SplitType = SplitType.Vertical;
        Node n2 = new Node();
        n2.Bounds.X = _ToSplit.Bounds.X;
        n2.Bounds.Y = _ToSplit.Bounds.Y + _Height + Padding;
        n2.Bounds.Width = _ToSplit.Bounds.Width;
        n2.Bounds.Height = _ToSplit.Bounds.Height - _Height - Padding;
        n2.SplitType = SplitType.Horizontal;
        if (n1.Bounds.Width > 0 && n1.Bounds.Height > 0)
            _List.Add(n1);
        if (n2.Bounds.Width > 0 && n2.Bounds.Height > 0)
            _List.Add(n2);
    }

    private void VerticalSplit(Node _ToSplit, int _Width, int _Height, List<Node> _List)
    {
        Node n1 = new Node();
        n1.Bounds.X = _ToSplit.Bounds.X + _Width + Padding;
        n1.Bounds.Y = _ToSplit.Bounds.Y;
        n1.Bounds.Width = _ToSplit.Bounds.Width - _Width - Padding;
        n1.Bounds.Height = _ToSplit.Bounds.Height;
        n1.SplitType = SplitType.Vertical;
        Node n2 = new Node();
        n2.Bounds.X = _ToSplit.Bounds.X;
        n2.Bounds.Y = _ToSplit.Bounds.Y + _Height + Padding;
        n2.Bounds.Width = _Width;
        n2.Bounds.Height = _ToSplit.Bounds.Height - _Height - Padding;
        n2.SplitType = SplitType.Horizontal;
        if (n1.Bounds.Width > 0 && n1.Bounds.Height > 0)
            _List.Add(n1);
        if (n2.Bounds.Width > 0 && n2.Bounds.Height > 0)
            _List.Add(n2);
    }

    private TextureInfo FindBestFitForNode(Node _Node, List<TextureInfo> _Textures)
    {
        TextureInfo bestFit = null;
        float nodeArea = _Node.Bounds.Width * _Node.Bounds.Height;
        float maxCriteria = 0.0f;
        foreach (TextureInfo ti in _Textures)
        {
            switch (FitHeuristic)
            {
                // Max of Width and Height ratios
                case BestFitHeuristic.MaxOneAxis:
                    if (ti.Width <= _Node.Bounds.Width && ti.Height <= _Node.Bounds.Height)
                    {
                        float wRatio = (float)ti.Width / (float)_Node.Bounds.Width;
                        float hRatio = (float)ti.Height / (float)_Node.Bounds.Height;
                        float ratio = wRatio > hRatio ? wRatio : hRatio;
                        if (ratio > maxCriteria)
                        {
                            maxCriteria = ratio;
                            bestFit = ti;
                        }
                    }
                    break;
                // Maximize Area coverage
                case BestFitHeuristic.Area:
                    if (ti.Width <= _Node.Bounds.Width && ti.Height <= _Node.Bounds.Height)
                    {
                        float textureArea = ti.Width * ti.Height;
                        float coverage = textureArea / nodeArea;
                        if (coverage > maxCriteria)
                        {
                            maxCriteria = coverage;
                            bestFit = ti;
                        }
                    }
                    break;
            }
        }
        return bestFit;
    }

    private List<TextureInfo> LayoutAtlas(List<TextureInfo> _Textures, Atlas _Atlas)
    {
        List<Node> freeList = new List<Node>();
        List<TextureInfo> textures = new List<TextureInfo>();
        _Atlas.Nodes = new List<Node>();
        textures = _Textures.ToList();
        Node root = new Node();
        root.Bounds.Width = _Atlas.Width;
        root.Bounds.Height = _Atlas.Height;
        root.SplitType = SplitType.Horizontal;
        freeList.Add(root);
        while (freeList.Count > 0 && textures.Count > 0)
        {
            Node node = freeList[0];
            freeList.RemoveAt(0);
            TextureInfo bestFit = FindBestFitForNode(node, textures);
            if (bestFit != null)
            {
                if (node.SplitType == SplitType.Horizontal)
                {
                    HorizontalSplit(node, bestFit.Width, bestFit.Height, freeList);
                }
                else
                {
                    VerticalSplit(node, bestFit.Width, bestFit.Height, freeList);
                }
                node.Texture = bestFit;
                node.Bounds.Width = bestFit.Width;
                node.Bounds.Height = bestFit.Height;
                textures.Remove(bestFit);
            }
            _Atlas.Nodes.Add(node);
        }
        return textures;
    }

    private MagickImage CreateAtlasImage(Atlas _Atlas)
    {
        MagickImage img = new(MagickColors.Transparent, (uint)_Atlas.Width, (uint)_Atlas.Height);
        foreach (Node n in _Atlas.Nodes)
        {
            if (n.Texture is not null)
            {
                using IMagickImage<byte> resizedSourceImg = TextureWorker.ResizeImage(n.Texture.Image, n.Bounds.Width, n.Bounds.Height);
                img.Composite(resizedSourceImg, n.Bounds.X, n.Bounds.Y, CompositeOperator.Copy);
            }
        }
        return img;
    }
}

public static SpriteType GetSpriteType(string path)
{
    string folderPath = Path.GetDirectoryName(path);
    string folderName = new DirectoryInfo(folderPath).Name;
    string lowerName = folderName.ToLower();

    if (lowerName == "backgrounds" || lowerName == "background")
    {
        return SpriteType.Background;
    }
    else if (lowerName == "fonts" || lowerName == "font")
    {
        return SpriteType.Font;
    }
    else if (lowerName == "sprites" || lowerName == "sprite")
    {
        return SpriteType.Sprite;
    }
    return SpriteType.Unknown;
}

string CheckValidity()
{
    bool recursiveCheck = true;
    if (!recursiveCheck)
        throw new ScriptException("Script cancelled.");

    // Get import folder
    /*string importFolder = PromptChooseDirectory();
    if (importFolder == null)
        throw new ScriptException("The import folder was not set.");*/

    var mainWindow = System.Windows.Application.Current.MainWindow as UndertaleModTool.MainWindow;

    string importFolder = mainWindow.LastImageDrop;

    //Stop the script if there's missing sprite entries or w/e.
    bool hadMessage = false;
    bool hadFramelessMessage = false;
    //string[] dirFiles = Directory.GetFiles(importFolder, "*.png", SearchOption.AllDirectories);

    string[] dirFiles = new string[1];
    dirFiles[0] = importFolder;

    foreach (string file in dirFiles)
    {
        string FileNameWithExtension = Path.GetFileName(file);
        string stripped = Path.GetFileNameWithoutExtension(file);
        int lastUnderscore = stripped.LastIndexOf('_');
        string spriteName = "";

        SpriteType spriteType = GetSpriteType(file);
        spriteType = SpriteType.Sprite;

        if ((spriteType != SpriteType.Sprite) && (spriteType != SpriteType.Background))
        {
            if (!hadMessage)
            {
                hadMessage = true;
                // this is annoying
                /*importAsSprite = ScriptQuestion(FileNameWithExtension + @" is in an incorrectly-named folder (valid names being ""Sprites"" and ""Backgrounds""). Would you like to import these images as sprites?
Pressing ""No"" will cause the program to ignore these images.");*/
                importAsSprite = true;
            }

            if (!importAsSprite)
            {
                continue;
            }
            else
            {
                spriteType = SpriteType.Sprite;
            }
        }
    }
    return importFolder;
}

public void OffsetResult()
{
    Form form = new Form()
    {
        Size = new Size(300, 200),
        Text = "Select Sprite Parameters",
        FormBorderStyle = FormBorderStyle.FixedDialog,
        MaximizeBox = false,
        MinimizeBox = false,
        StartPosition = FormStartPosition.CenterScreen,
        AutoScaleMode = AutoScaleMode.Dpi,
        AutoScaleDimensions = new Size(96, 96),
    };

    Func<int, int, Size> logicalSize = (w, h) => form.LogicalToDeviceUnits(new Size(w, h));

    ToolTip toolTip = new ToolTip();

    // for some reason the labels cover eachother on hi-dpi screens,
    // so this is a bit of a hack for that
    int labelCover = 3;

    Label specialLabel = new Label();
    specialLabel.Location = new Point(5, 10);
    specialLabel.Text = "Special Version:";
    specialLabel.Size = logicalSize(110, 30 - labelCover);
    form.Controls.Add(specialLabel);

    CheckBox isSpecialBox = new System.Windows.Forms.CheckBox();
    isSpecialBox.Enabled = Data.IsGameMaker2();
    isSpecialBox.Location = new Point(specialLabel.Width + 5, 10);
    isSpecialBox.Size = logicalSize(20, 20);
    toolTip.SetToolTip(isSpecialBox, "Is special type? (required for setting animation speed)");
    form.Controls.Add(isSpecialBox);

    TextBox specialVerBox = new System.Windows.Forms.TextBox();
    specialVerBox.Enabled = Data.IsGameMaker2();
    specialVerBox.AcceptsReturn = false;
    specialVerBox.AcceptsTab = false;
    specialVerBox.AutoSize = true;
    specialVerBox.Multiline = false;
    specialVerBox.Text = "1";
    specialVerBox.Name = "Special Version";
    specialVerBox.Location = new Point(specialLabel.Width + 5 + isSpecialBox.Width, 10);
    specialVerBox.Size = logicalSize(30, 30);
    specialVerBox.Anchor = AnchorStyles.Right;
    form.Controls.Add(specialVerBox);

    Label label1 = new Label();
    label1.Location = new Point(5, specialVerBox.Height + 15);
    label1.Text = "Animation Speed:";
    label1.Size = logicalSize(110, 30 - labelCover);
    form.Controls.Add(label1);

    TextBox textBox = new System.Windows.Forms.TextBox();
    textBox.Enabled = Data.IsGameMaker2();
    textBox.AcceptsReturn = false;
    textBox.AcceptsTab = false;
    textBox.AutoSize = true;
    textBox.Multiline = false;
    textBox.Text = "1";
    textBox.Name = "Animation Speed";
    textBox.Location = new Point(label1.Width + 5, specialVerBox.Height + 15);
    textBox.Size = logicalSize(30, 30);
    textBox.Anchor = AnchorStyles.Right;
    form.Controls.Add(textBox);

    Label label2 = new Label();
    label2.Location = new Point(5, 20 + specialVerBox.Height + textBox.Height);
    label2.Text = "Playback Type:";
    label2.Size = logicalSize(110, 30 - labelCover);
    form.Controls.Add(label2);

    ComboBox comboBox = new ComboBox();
    comboBox.Enabled = Data.IsGameMaker2();
    comboBox.Name = "Playback Type";
    comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
    comboBox.Location = new Point(label2.Width + 5, 20 + specialVerBox.Height + textBox.Height);
    comboBox.Size = logicalSize(160, 30);
    comboBox.Anchor = AnchorStyles.Right;
    foreach (string play in playbacks)
        comboBox.Items.Add(play);
    int defaultSelection = comboBox.Items.IndexOf("Frames Per Game Frame");
    comboBox.SelectedIndex = defaultSelection == -1 ? 0 : defaultSelection;
    form.Controls.Add(comboBox);

    Label label3 = new Label();
    label3.Location = new Point(5, 25 + specialVerBox.Height + textBox.Height + comboBox.Height);
    label3.Text = "Origin Position:";
    label3.Size = logicalSize(110, 30 - labelCover);
    form.Controls.Add(label3);

    ComboBox comboBox2 = new ComboBox();
    comboBox2.Name = "Origin Position";
    comboBox2.DropDownStyle = ComboBoxStyle.DropDownList;
    comboBox2.Location = new Point(label2.Width + 5, 25 + specialVerBox.Height + textBox.Height + comboBox.Height);
    comboBox2.Size = logicalSize(160, 30);
    comboBox2.Anchor = AnchorStyles.Right;
    foreach (string off in offsets)
        comboBox2.Items.Add(off);
    int defaultSelection2 = comboBox2.Items.IndexOf("Top Left");
    comboBox2.SelectedIndex = defaultSelection2 == -1 ? 0 : defaultSelection2;
    form.Controls.Add(comboBox2);

    int bottomY = form.Size.Height - 30;

    Button okBtn = new Button();
    okBtn.Text = "&Confirm";
    okBtn.Size = logicalSize(90, 30);
    okBtn.Location = new Point(5, 35 + specialVerBox.Height + textBox.Height + comboBox.Height + comboBox2.Height);
    okBtn.Anchor = AnchorStyles.Left;
    form.Controls.Add(okBtn);

    EventHandler updateFramesActive = (o, e) =>
    {
        specialVerBox.Enabled = isSpecialBox.Checked;
        textBox.Enabled = isSpecialBox.Checked;
    };

    isSpecialBox.CheckedChanged += updateFramesActive;
    updateFramesActive(null, null);

    okBtn.Click += (o, e) =>
    {
        if (float.TryParse(textBox.Text, out float j))
        {
            if (uint.TryParse(specialVerBox.Text, out uint k))
            {
                isSpecial = isSpecialBox.Checked;
                specialVer = k;
                animSpd = j;
                offresult = offsets[comboBox2.SelectedIndex];
                playback = comboBox.SelectedIndex;
                form.Close();
            }
            else
            {
                MessageBox.Show("Please use a number in the special version.");
            }
        }
        else
        {
            MessageBox.Show("Please use a number in the animation speed.");
        }
    };
    form.ShowDialog();
}
