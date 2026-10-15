using Ryujinx.Graphics.Texture;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Ryujinx.HLE.HOS
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The page of a friend (the MyPage applet, 22.5.0), as a profile of
    /// today: a wide banner with rounded corners at the top of the right column (the friend's
    /// Nextendo badges are drawn on it), and the profile pictures with rounded corners.
    /// <para>
    /// The layout of the page (FrdCntDetail) gets one picture, and the material of the profile
    /// pictures a second texture that cuts their corners. Both textures take the entries of two
    /// that the page no longer shows (the Nintendo Network ID and Nintendo 3DS friend suggestions,
    /// gone from the console), with room of their own added at the end of the texture archive.
    /// The emulator paints the banner for the friend shown (see NextendoFriendProfile).
    /// </para>
    /// </summary>
    public static class NextendoFriendPage
    {
        public const string Archive = "/lyt/MyPage.szs";

        private const string Layout = "blyt/FrdCntDetail.bflyt";
        private const string Textures = "timg/__Combined.bntx";

        private const string OwnLayout = "blyt/MypMyStatus.bflyt";

        // Textures no layout of the applet uses (checked on 22.5.0). The illustrations of the
        // friend suggestions (3DS, Wii U) are still shown there: they must stay as they are.
        private const string BannerTexture = "IcoFrdAddPanel_01_32^t";
        private const string OwnBannerTexture = "IcoFrdAddPanel_03_64^t";
        private const string MaskTexture = "IcoFrdAddPanel_01_64^t";

        // A round mask, for the pictures of people (round as on the console).
        private const string CircleTexture = "IcoFrdAddPanel_02_64^t";

        // The layouts whose picture gets round corners (the icons of the games in the rows of a
        // page) or is made round (the profile picture of a friend's page), the material of the
        // picture in each and the mask it gets.
        private static readonly Dictionary<string, (string Material, string Mask)> _pictures = new()
        {
            ["blyt/MypUserIcon.bflyt"] = ("P_Image", CircleTexture),
            ["blyt/MypBtnPresencePlate.bflyt"] = ("P_Icon", MaskTexture),
            ["blyt/MypBtnGamePlate.bflyt"] = ("P_Icon", MaskTexture),
            ["blyt/MypBtnPresence.bflyt"] = ("P_Icon", MaskTexture),
            ["blyt/MypBtnGame.bflyt"] = ("P_Icon", MaskTexture),
        };

        // The friend list, as the console's newer HOME Menu draws it: each friend a plain card
        // with a round picture inside its margins, the nickname and the last time online under it.
        private const string FriendTileLayout = "blyt/FrdBtnList.bflyt";
        private static readonly string[] _friendTilePictures = ["P_Image", "P_NoImage", "P_RecoUser"];
        private const float FriendPictureScale = 0.72f;   // 170 -> about 122 pixels
        private const float FriendPictureY = 46f;         // a margin above it, as wide as at its sides
        private const float FriendTextY = -56f;           // the nickname and the status right under it
        private const float FriendCardRadius = 18f;

        // The square plate behind a profile picture: it would show at the corners.
        private static readonly string[] _platePanes = ["P_CardBase"];

        // The user's own picture (a button of their page) is drawn by the system's own shapes
        // (shadow, border): it has a setting of its own for round corners, the one the round
        // pencil mark on it uses. The picture is 396 wide there, its cursor a little more: half of
        // it makes it round, as the pictures of people are on the console.
        private const string OwnPictureLayout = "blyt/MypBtnUserIcon.bflyt";
        private const string RoundSetting = "S_RoundRadius";
        private const float OwnPictureRadius = 198f;

        // A size no other texture of the applet has: the emulator finds the banner by it. Twice
        // the size it shows at, for the picture to stay sharp when the emulator draws larger.
        public const int BannerWidth = 1148, BannerHeight = 224;

        // The banner of the own page: another width, for the one of a friend never to be painted over it.
        public const int OwnBannerWidth = 1152;

        /// <summary>
        /// The first 64 bytes of the banner texture of a friend's page (16 pixels, all of them
        /// transparent: they sit outside its round corner). On a console, the module that paints
        /// the banner inside the applet finds the texture in memory by them, and leaves them there.
        /// </summary>
        public static ReadOnlySpan<byte> BannerMark =>
        [
            (byte)'N', (byte)'X', (byte)'T', 0, (byte)'B', (byte)'A', (byte)'N', 0, (byte)'N', (byte)'E', (byte)'R', 0, (byte)'F', (byte)'R', (byte)'D', 0,
            (byte)'v', (byte)'0', (byte)'1', 0, (byte)'m', (byte)'y', (byte)'P', 0, (byte)'a', (byte)'g', (byte)'e', 0, (byte)'2', (byte)'2', (byte)'5', 0,
            0x4E, 0x58, 0x54, 0, 0x13, 0x10, 0x00, 0, 0xA5, 0x5A, 0xC3, 0, 0x3C, 0x96, 0x69, 0, 0x0F, 0xF0, 0x55, 0, 0xAA, 0x11, 0xEE, 0, 0x77, 0x88, 0x24, 0, 0xDB, 0x42, 0xBD, 0,
        ];

        private const int MaskSize = 256;
        private const float MaskRadius = 52f;

        // The right column: 574 wide, its top margin ("N_Top", 40 high) is made the banner's room.
        private const float TopHeight = 152f;
        private static readonly (float X, float Y, float W, float H) _banner = (0f, -80f, 574f, 112f);

        // The own page of the user: 720 wide, under the title of the applet; all of it goes down
        // by the room the banner takes.
        private const float OwnShift = 132f;
        private static readonly (float X, float Y, float W, float H) _ownBanner = (0f, -174f, 720f, 140f);

        /// <summary>The layout archive of the applet (not compressed) with the banner and the round corners, or null.</summary>
        /// <param name="ownBanner">The banner of the own profile of the user (OwnBannerWidth x BannerHeight, four bytes a pixel), or null.</param>
        /// <param name="friendBanner">
        /// False leaves the page of a friend without its banner (and the room made for it): on a
        /// console nothing paints that banner yet, the room would stay empty.
        /// </param>
        public static byte[] Patch(byte[] ownBanner, byte[] archive, bool friendBanner = true)
        {
            List<(string Name, byte[] Data)> files = ReadArchive(archive, out uint multiplier);
            byte[] page = files.Find(f => f.Name == Layout).Data;
            bool layoutDone = false, texturesDone = false;

            for (int i = 0; i < files.Count; i++)
            {
                string name = files[i].Name;

                if (name == Layout && !friendBanner)
                {
                    layoutDone = true;
                }
                else if (name == Layout && PatchPage(files[i].Data) is { } layout)
                {
                    files[i] = (name, layout);
                    layoutDone = true;
                }
                else if (_pictures.TryGetValue(name, out (string Material, string Mask) round) && PatchPicture(files[i].Data, [round.Material], round.Mask) is { } picture)
                {
                    files[i] = (name, picture);
                }
                else if (name == FriendTileLayout && PatchFriendTile(files[i].Data) is { } tile)
                {
                    files[i] = (name, tile);
                }
                else if (name == OwnPictureLayout && PatchOwnPicture(files[i].Data) is { } rounded)
                {
                    files[i] = (name, rounded);
                }
                else if (name == OwnLayout && page != null && PatchOwnPage(files[i].Data, page) is { } own)
                {
                    files[i] = (name, own);
                }
                else if (name == Textures && PatchTextures(files[i].Data, ownBanner, friendBanner) is { } textures)
                {
                    files[i] = (name, textures);
                    texturesDone = true;
                }
            }

            return layoutDone && texturesDone ? WriteArchive(files, multiplier) : null;
        }

        // ------------------------------------------------------------------ the friend notification

        public const string NotificationArchive = "/lyt/OverlayNotification.szs";

        private const string NotificationLayout = "blyt/OvnFriend.bflyt";

        // "A friend is online" (overlay applet, 22.5.0): the friend's picture is a small one next
        // to the name, with nothing under it. It is made to take both lines, name and game.
        private static readonly (float X, float Y, float Scale) _notificationPicture = (-131f, 0f, 1.35f);

        /// <summary>The notification archive of the overlay applet (not compressed) with a larger friend picture, or null.</summary>
        public static byte[] PatchNotification(byte[] archive)
        {
            List<(string Name, byte[] Data)> files = ReadArchive(archive, out uint multiplier);
            int at = files.FindIndex(f => f.Name == NotificationLayout);

            if (at < 0)
            {
                return null;
            }

            List<byte[]> sections = Sections(files[at].Data, out int header);
            int shown = sections.FindIndex(s => Kind(s) == "pan1" && PaneName(s) == "N_OpenFriend");
            byte[] picture = shown < 0 ? null : sections.Skip(shown).FirstOrDefault(s => Kind(s) == "pan1" && PaneName(s) == "N_AvatarS");

            if (picture == null)
            {
                return null;
            }

            BinaryPrimitives.WriteSingleLittleEndian(picture.AsSpan(0x2C), _notificationPicture.X);
            BinaryPrimitives.WriteSingleLittleEndian(picture.AsSpan(0x30), _notificationPicture.Y);
            BinaryPrimitives.WriteSingleLittleEndian(picture.AsSpan(0x44), _notificationPicture.Scale);
            BinaryPrimitives.WriteSingleLittleEndian(picture.AsSpan(0x48), _notificationPicture.Scale);

            files[at] = (NotificationLayout, Layout_(files[at].Data, header, sections));

            return WriteArchive(files, multiplier);
        }

        // ------------------------------------------------------------------ the archive (SARC)

        private static List<(string Name, byte[] Data)> ReadArchive(byte[] b, out uint multiplier)
        {
            int header = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(4));
            int data = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0xC));
            int count = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(header + 6));

            multiplier = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(header + 8));

            int nodes = header + 0xC;
            int names = nodes + count * 0x10 + 8;
            List<(string, byte[])> files = [];

            for (int k = 0; k < count; k++)
            {
                int attributes = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(nodes + k * 16 + 4));
                int start = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(nodes + k * 16 + 8));
                int end = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(nodes + k * 16 + 12));
                int name = names + (attributes & 0xFFFFFF) * 4;
                int nameEnd = Array.IndexOf(b, (byte)0, name);

                files.Add((Encoding.ASCII.GetString(b, name, nameEnd - name), b.AsSpan(data + start, end - start).ToArray()));
            }

            return files;
        }

        private static uint Hash(string name, uint multiplier)
        {
            uint hash = 0;

            foreach (byte c in Encoding.ASCII.GetBytes(name))
            {
                hash = hash * multiplier + c;
            }

            return hash;
        }

        private static byte[] WriteArchive(List<(string Name, byte[] Data)> files, uint multiplier)
        {
            const int Alignment = 0x2000; // the textures are used where they are loaded

            files = [.. files.OrderBy(f => Hash(f.Name, multiplier))];

            using MemoryStream names = new();
            using MemoryStream table = new();
            using MemoryStream data = new();
            using BinaryWriter tableWriter = new(table);

            tableWriter.Write(Encoding.ASCII.GetBytes("SFAT"));
            tableWriter.Write((ushort)0xC);
            tableWriter.Write((ushort)files.Count);
            tableWriter.Write(multiplier);

            foreach ((string name, byte[] content) in files)
            {
                int nameAt = (int)names.Length / 4;

                names.Write(Encoding.ASCII.GetBytes(name));
                names.WriteByte(0);

                while (names.Length % 4 != 0)
                {
                    names.WriteByte(0);
                }

                int alignment = name.EndsWith(".bntx", StringComparison.Ordinal) ? Alignment : 8;

                while (data.Length % alignment != 0)
                {
                    data.WriteByte(0);
                }

                tableWriter.Write(Hash(name, multiplier));
                tableWriter.Write(0x01000000 | nameAt);
                tableWriter.Write((int)data.Length);
                tableWriter.Write((int)data.Length + content.Length);
                data.Write(content);
            }

            int head = 0x14 + (int)table.Length + 8 + (int)names.Length;
            int start = (head + Alignment - 1) / Alignment * Alignment;

            using MemoryStream archive = new();
            using BinaryWriter writer = new(archive);

            writer.Write(Encoding.ASCII.GetBytes("SARC"));
            writer.Write((ushort)0x14);
            writer.Write((ushort)0xFEFF);
            writer.Write(start + (int)data.Length);
            writer.Write(start);
            writer.Write((ushort)0x100);
            writer.Write((ushort)0);
            writer.Write(table.ToArray());
            writer.Write(Encoding.ASCII.GetBytes("SFNT"));
            writer.Write((ushort)8);
            writer.Write((ushort)0);
            writer.Write(names.ToArray());
            writer.Write(new byte[start - head]);
            writer.Write(data.ToArray());

            return archive.ToArray();
        }

        // ------------------------------------------------------------------ the layouts (FLYT)

        private static string PaneName(byte[] section) => Encoding.ASCII.GetString(section, 0xC, 0x18).Split('\0')[0];

        private static string MaterialName(byte[] material) => Encoding.ASCII.GetString(material, 0, 0x1C).Split('\0')[0];

        private static string Kind(byte[] section) => Encoding.ASCII.GetString(section, 0, 4);

        private static List<byte[]> Sections(byte[] layout, out int header)
        {
            header = BinaryPrimitives.ReadUInt16LittleEndian(layout.AsSpan(6));

            int count = BinaryPrimitives.ReadUInt16LittleEndian(layout.AsSpan(0x10));
            List<byte[]> sections = [];

            for (int at = header, i = 0; i < count; i++)
            {
                int size = BinaryPrimitives.ReadInt32LittleEndian(layout.AsSpan(at + 4));

                sections.Add(layout.AsSpan(at, size).ToArray());
                at += size;
            }

            return sections;
        }

        private static byte[] Layout_(byte[] layout, int header, List<byte[]> sections)
        {
            using MemoryStream output = new();

            output.Write(layout, 0, header);

            foreach (byte[] s in sections)
            {
                output.Write(s);
            }

            byte[] made = output.ToArray();

            BinaryPrimitives.WriteInt32LittleEndian(made.AsSpan(0xC), made.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(made.AsSpan(0x10), (ushort)sections.Count);

            return made;
        }

        // One more name in the list of the textures a layout uses: its number there, or -1.
        private static int AddTexture(List<byte[]> sections, string added)
        {
            int at = sections.FindIndex(s => Kind(s) == "txl1");

            if (at < 0)
            {
                // a layout that uses none: the list comes before the fonts and the materials
                at = sections.FindIndex(s => Kind(s) is "fnl1" or "mat1");

                if (at < 0)
                {
                    return -1;
                }

                sections.Insert(at, [.. "txl1"u8, 12, 0, 0, 0, 0, 0, 0, 0]);
            }

            byte[] s = sections[at];
            int n = BinaryPrimitives.ReadUInt16LittleEndian(s.AsSpan(8));
            List<string> textures = [];

            for (int t = 0; t < n; t++)
            {
                int name = 12 + BinaryPrimitives.ReadInt32LittleEndian(s.AsSpan(12 + 4 * t));

                textures.Add(Encoding.ASCII.GetString(s, name, Array.IndexOf(s, (byte)0, name) - name));
            }

            textures.Add(added);

            using MemoryStream blob = new();
            using MemoryStream offsets = new();

            foreach (string texture in textures)
            {
                offsets.Write(BitConverter.GetBytes(4 * textures.Count + (int)blob.Length));
                blob.Write(Encoding.ASCII.GetBytes(texture));
                blob.WriteByte(0);
            }

            while ((offsets.Length + blob.Length) % 4 != 0)
            {
                blob.WriteByte(0);
            }

            using MemoryStream section = new();

            section.Write(Encoding.ASCII.GetBytes("txl1"));
            section.Write(BitConverter.GetBytes(12 + (int)offsets.Length + (int)blob.Length));
            section.Write(BitConverter.GetBytes((ushort)textures.Count));
            section.Write(new byte[2]);
            section.Write(offsets.ToArray());
            section.Write(blob.ToArray());

            sections[at] = section.ToArray();

            return textures.Count - 1;
        }

        private static List<byte[]> Materials(List<byte[]> sections, out int at)
        {
            at = sections.FindIndex(s => Kind(s) == "mat1");

            if (at < 0)
            {
                return null;
            }

            byte[] s = sections[at];
            int n = BinaryPrimitives.ReadUInt16LittleEndian(s.AsSpan(8));
            List<byte[]> materials = [];

            for (int m = 0; m < n; m++)
            {
                int start = BinaryPrimitives.ReadInt32LittleEndian(s.AsSpan(12 + 4 * m));
                int end = m + 1 < n ? BinaryPrimitives.ReadInt32LittleEndian(s.AsSpan(12 + 4 * (m + 1))) : s.Length;

                materials.Add(s.AsSpan(start, end - start).ToArray());
            }

            return materials;
        }

        private static byte[] MaterialSection(List<byte[]> materials)
        {
            using MemoryStream blob = new();
            using MemoryStream offsets = new();

            foreach (byte[] material in materials)
            {
                offsets.Write(BitConverter.GetBytes(12 + 4 * materials.Count + (int)blob.Length));
                blob.Write(material);
            }

            using MemoryStream section = new();

            section.Write(Encoding.ASCII.GetBytes("mat1"));
            section.Write(BitConverter.GetBytes(12 + (int)offsets.Length + (int)blob.Length));
            section.Write(BitConverter.GetBytes((ushort)materials.Count));
            section.Write(new byte[2]);
            section.Write(offsets.ToArray());
            section.Write(blob.ToArray());

            return section.ToArray();
        }

        // The page: the banner, in the top margin of the right column (it scrolls with the rest).
        private static byte[] PatchPage(byte[] layout)
        {
            List<byte[]> sections = Sections(layout, out int header);
            int texture = AddTexture(sections, BannerTexture);
            List<byte[]> materials = Materials(sections, out int materialsAt);
            byte[] model = materials?.Find(m => MaterialName(m) == "P_Pict");
            byte[] picture = sections.Find(s => Kind(s) == "pic1" && PaneName(s) == "P_LineVertical");
            int top = sections.FindIndex(s => Kind(s) == "pan1" && PaneName(s) == "N_Top");

            if (texture < 0 || model == null || picture == null || top < 0)
            {
                return null;
            }

            materials.Add(BannerMaterial(model, texture));
            sections[materialsAt] = MaterialSection(materials);

            byte[] banner = BannerPane(picture, _banner, materials.Count - 1);

            BinaryPrimitives.WriteSingleLittleEndian(sections[top].AsSpan(0x50), TopHeight);

            // The applet draws the rows of the column one by one, the ones it knows: the banner is
            // made a part of the first one (the friend's status), right under the margin.
            int row = sections.FindIndex(s => Kind(s) == "pan1" && PaneName(s) == "N_Presence");
            int inside = row < 0 ? -1 : sections.FindIndex(row, s => Kind(s) == "pas1");

            if (inside < 0 || inside > row + 2)
            {
                return null;
            }

            BinaryPrimitives.WriteSingleLittleEndian(banner.AsSpan(0x30), TopHeight + _banner.Y);
            sections.Insert(inside + 1, banner);

            return Layout_(layout, header, sections);
        }

        // The material of a banner: the one of a plain picture of the page of a friend, with its texture.
        private static byte[] BannerMaterial(byte[] model, int texture)
        {
            byte[] material = (byte[])model.Clone();

            Array.Clear(material, 0, 0x1C);
            Encoding.ASCII.GetBytes("P_NxBanner").CopyTo(material, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(material.AsSpan(0x24), 0x00000000); // no colour added
            BinaryPrimitives.WriteUInt32LittleEndian(material.AsSpan(0x28), 0xFFFFFFFF); // the texture as it is
            BinaryPrimitives.WriteUInt16LittleEndian(material.AsSpan(0x2C), (ushort)texture);

            return material;
        }

        private static byte[] BannerPane(byte[] picture, (float X, float Y, float W, float H) place, int material)
        {
            byte[] banner = (byte[])picture.Clone();

            banner[8] = 1;    // shown
            banner[9] = 0;    // placed by its centre
            banner[10] = 255; // opaque
            Array.Clear(banner, 0xC, 0x18);
            Encoding.ASCII.GetBytes("P_NxBanner").CopyTo(banner, 0xC);
            BinaryPrimitives.WriteSingleLittleEndian(banner.AsSpan(0x2C), place.X);
            BinaryPrimitives.WriteSingleLittleEndian(banner.AsSpan(0x30), place.Y);
            BinaryPrimitives.WriteSingleLittleEndian(banner.AsSpan(0x4C), place.W);
            BinaryPrimitives.WriteSingleLittleEndian(banner.AsSpan(0x50), place.H);
            banner.AsSpan(0x54, 16).Fill(0xFF); // the four corners: the colours of the texture
            BinaryPrimitives.WriteUInt16LittleEndian(banner.AsSpan(0x54 + 16), (ushort)material);

            return banner;
        }

        // The own page of the user: their banner above their picture and name, the rest moved down.
        private static byte[] PatchOwnPage(byte[] layout, byte[] friendPage)
        {
            List<byte[]> models = Sections(friendPage, out _);
            byte[] model = Materials(models, out _)?.Find(m => MaterialName(m) == "P_Pict");
            byte[] picture = models.Find(s => Kind(s) == "pic1" && PaneName(s) == "P_LineVertical");

            List<byte[]> sections = Sections(layout, out int header);
            int texture = AddTexture(sections, OwnBannerTexture);
            List<byte[]> materials = Materials(sections, out int materialsAt);
            int top = sections.FindIndex(s => Kind(s) == "pan1" && PaneName(s) == "N_Top");
            int inside = top < 0 ? -1 : sections.FindIndex(top, s => Kind(s) == "pas1");

            if (model == null || picture == null || texture < 0 || materials == null || inside < 0 || inside > top + 2)
            {
                return null;
            }

            materials.Add(BannerMaterial(model, texture));
            sections[materialsAt] = MaterialSection(materials);

            foreach (byte[] s in sections.Where(s => Kind(s) == "pan1"))
            {
                string name = PaneName(s);

                if (name == "N_Top")
                {
                    BinaryPrimitives.WriteSingleLittleEndian(s.AsSpan(0x50), BinaryPrimitives.ReadSingleLittleEndian(s.AsSpan(0x50)) + OwnShift);
                }
                else if (name is "N_User" or "N_NoNA" or "N_HeaderLogList" or "N_Btm" || name.StartsWith("N_LogList_", StringComparison.Ordinal))
                {
                    BinaryPrimitives.WriteSingleLittleEndian(s.AsSpan(0x30), BinaryPrimitives.ReadSingleLittleEndian(s.AsSpan(0x30)) - OwnShift);
                }
            }

            sections.Insert(inside + 1, BannerPane(picture, _ownBanner, materials.Count - 1));

            return Layout_(layout, header, sections);
        }

        // The own picture of the user: round corners, by the setting of the system, on the
        // picture and on the cursor around it.
        private static byte[] PatchOwnPicture(byte[] layout)
        {
            List<byte[]> sections = Sections(layout, out int header);
            bool done = false;

            for (int i = 0; i + 1 < sections.Count; i++)
            {
                if (Kind(sections[i + 1]) != "usd1" || Kind(sections[i]) is not ("pic1" or "pan1"))
                {
                    continue;
                }

                string name = PaneName(sections[i]);

                if (name is "P_Image" or "P_BtnShdw" or "N_BtnFocusKey" or "P_InnerCursor" && Round(sections[i + 1], OwnPictureRadius) is { } settings)
                {
                    sections[i + 1] = settings;
                    done |= name == "P_Image";
                }
            }

            return done ? Layout_(layout, header, sections) : null;
        }

        // The settings of a pane ("usd1"), with the radius of its corners set (added when it has none).
        private static byte[] Round(byte[] settings, float radius)
        {
            int count = BinaryPrimitives.ReadUInt16LittleEndian(settings.AsSpan(8));
            List<(string Name, byte Type, ushort Count, byte[] Data)> entries = [];

            for (int i = 0; i < count; i++)
            {
                int at = 12 + i * 12;
                int nameAt = at + BinaryPrimitives.ReadInt32LittleEndian(settings.AsSpan(at));
                int dataAt = at + BinaryPrimitives.ReadInt32LittleEndian(settings.AsSpan(at + 4));
                ushort number = BinaryPrimitives.ReadUInt16LittleEndian(settings.AsSpan(at + 8));
                byte type = settings[at + 10];

                // texts, whole numbers and numbers: the kinds these panes have
                if (type > 2)
                {
                    return null;
                }

                string name = Encoding.ASCII.GetString(settings, nameAt, Array.IndexOf(settings, (byte)0, nameAt) - nameAt);

                entries.Add((name, type, number, settings.AsSpan(dataAt, type == 0 ? number : number * 4).ToArray()));
            }

            byte[] value = BitConverter.GetBytes(radius);
            int round = entries.FindIndex(e => e.Name == RoundSetting);

            if (round >= 0)
            {
                entries[round] = (RoundSetting, 2, 1, value);
            }
            else
            {
                entries.Add((RoundSetting, 2, 1, value));
            }

            using MemoryStream table = new();
            using MemoryStream data = new();
            using MemoryStream names = new();

            int dataStart = 12 + entries.Count * 12;
            int dataSize = entries.Sum(e => (e.Data.Length + (e.Type == 0 ? 1 : 0) + 3) / 4 * 4);

            for (int i = 0; i < entries.Count; i++)
            {
                int at = 12 + i * 12;

                table.Write(BitConverter.GetBytes(dataStart + dataSize + (int)names.Length - at));
                table.Write(BitConverter.GetBytes(dataStart + (int)data.Length - at));
                table.Write(BitConverter.GetBytes(entries[i].Count));
                table.WriteByte(entries[i].Type);
                table.WriteByte(0);

                data.Write(entries[i].Data);

                if (entries[i].Type == 0)
                {
                    data.WriteByte(0);
                }

                while (data.Length % 4 != 0)
                {
                    data.WriteByte(0);
                }

                names.Write(Encoding.ASCII.GetBytes(entries[i].Name));
                names.WriteByte(0);
            }

            while (names.Length % 4 != 0)
            {
                names.WriteByte(0);
            }

            using MemoryStream section = new();

            section.Write(settings, 0, 4);
            section.Write(BitConverter.GetBytes(dataStart + (int)data.Length + (int)names.Length));
            section.Write(BitConverter.GetBytes((ushort)entries.Count));
            section.Write(settings, 10, 2);
            section.Write(table.ToArray());
            section.Write(data.ToArray());
            section.Write(names.ToArray());

            return section.ToArray();
        }

        // A profile picture: a second texture on its material, that cuts the corners round (or
        // all of it round, with the round mask).
        private static byte[] PatchPicture(byte[] layout, string[] pictureMaterials, string mask)
        {
            List<byte[]> sections = Sections(layout, out int header);

            return PatchPictures(sections, pictureMaterials, mask) ? Layout_(layout, header, sections) : null;
        }

        private static bool PatchPictures(List<byte[]> sections, string[] pictureMaterials, string mask)
        {
            List<byte[]> materials = Materials(sections, out int materialsAt);
            int texture = -1;
            bool done = false;

            foreach (string pictureMaterial in pictureMaterials)
            {
                int at = materials?.FindIndex(m => MaterialName(m) == pictureMaterial) ?? -1;

                // the material as the applet has it: one texture, drawn as it is
                if (at < 0 || materials[at].Length != 0x54 || BinaryPrimitives.ReadUInt32LittleEndian(materials[at].AsSpan(0x1C)) != 0x15)
                {
                    continue;
                }

                if (texture < 0)
                {
                    texture = AddTexture(sections, mask);

                    if (texture < 0)
                    {
                        return false;
                    }
                }

                materials[at] = MaskedMaterial(materials[at], texture);
                done = true;
            }

            if (!done)
            {
                return false;
            }

            sections[materialsAt] = MaterialSection(materials);

            foreach (byte[] plate in sections.Where(s => Kind(s) == "pic1" && _platePanes.Contains(PaneName(s))))
            {
                plate[8] &= 0xFE; // not shown
            }

            return true;
        }

        private static byte[] MaskedMaterial(byte[] was, int texture)
        {

            using MemoryStream material = new();

            material.Write(was, 0, 0x1C);                        // its name
            material.Write(BitConverter.GetBytes(0x6A));         // two textures, placed alike, combined once
            material.Write(was, 0x20, 0xC);                      // its colours
            material.Write(was, 0x2C, 4);                        // the picture
            material.Write(BitConverter.GetBytes((ushort)texture));
            material.Write([0, 0]);                              // the mask: clamped at its edges
            material.Write(was, 0x30, 0x14);                     // where the picture is on the pane
            material.Write(was, 0x30, 0x14);                     // the mask: the same
            material.Write(was, 0x44, 0x10);                     // both from the coordinates of the pane
            material.Write(was, 0x44, 0x10);
            material.Write([1, 1, 0, 0]);                        // colour and opacity: one times the other

            return material.ToArray();
        }

        // A friend of the list: the picture smaller and round, in the card's margins; the
        // nickname and the status under it; the card and its cursor with rounder corners.
        private static byte[] PatchFriendTile(byte[] layout)
        {
            List<byte[]> sections = Sections(layout, out int header);

            if (!PatchPictures(sections, _friendTilePictures, CircleTexture))
            {
                return null;
            }

            byte[] Pane(string name) => sections.FirstOrDefault(s => Kind(s) is "pan1" or "pic1" && PaneName(s) == name);

            if (Pane("N_Image") is not { } image || Pane("N_Text") is not { } text)
            {
                return null;
            }

            BinaryPrimitives.WriteSingleLittleEndian(image.AsSpan(0x30), FriendPictureY);
            BinaryPrimitives.WriteSingleLittleEndian(image.AsSpan(0x44), FriendPictureScale);
            BinaryPrimitives.WriteSingleLittleEndian(image.AsSpan(0x48), FriendPictureScale);
            BinaryPrimitives.WriteSingleLittleEndian(text.AsSpan(0x30), FriendTextY);

            // the plate behind the texts: the card is all one colour, as on the console
            if (Pane("P_BgText") is { } textPlate)
            {
                textPlate[8] &= 0xFE;
            }

            for (int i = 0; i + 1 < sections.Count; i++)
            {
                if (Kind(sections[i + 1]) == "usd1" && Kind(sections[i]) is "pic1" or "pan1" &&
                    PaneName(sections[i]) is "P_BtnCardBase" or "P_BtnCardBaseInv" or "P_BtnDecideKey" or "P_BtnTouch" or "N_BtnFocusKey" or "P_InnerCursor" or "P_BgAll" &&
                    Round(sections[i + 1], FriendCardRadius) is { } settings)
                {
                    sections[i + 1] = settings;
                }
            }

            return Layout_(layout, header, sections);
        }

        // ------------------------------------------------------------------ the textures (BNTX)

        private static byte[] PatchTextures(byte[] bntx, byte[] ownBanner, bool friendBanner)
        {
            // the corners of the profile pictures: white, opaque inside a rounded square
            byte[] mask = new byte[MaskSize * MaskSize];

            for (int y = 0; y < MaskSize; y++)
            {
                for (int x = 0; x < MaskSize; x++)
                {
                    float dx = MathF.Max(MathF.Max(MaskRadius - (x + 0.5f), x + 0.5f - (MaskSize - MaskRadius)), 0);
                    float dy = MathF.Max(MathF.Max(MaskRadius - (y + 0.5f), y + 0.5f - (MaskSize - MaskRadius)), 0);

                    mask[y * MaskSize + x] = (byte)(Math.Clamp(MaskRadius - MathF.Sqrt(dx * dx + dy * dy) + 0.5f, 0f, 1f) * 255f);
                }
            }

            const int BlockHeightLog2 = 4; // rows of 128 pixels

            byte[] maskData = new byte[MaskSize * MaskSize];

            LayoutConverter.ConvertLinearToBlockLinear(maskData, MaskSize, MaskSize, MaskSize, 1, 1 << BlockHeightLog2, mask);

            // the round one: a disc, its edge smoothed over a pixel
            byte[] circle = new byte[MaskSize * MaskSize];
            float centre = MaskSize / 2f;

            for (int y = 0; y < MaskSize; y++)
            {
                for (int x = 0; x < MaskSize; x++)
                {
                    float d = MathF.Sqrt((x + 0.5f - centre) * (x + 0.5f - centre) + (y + 0.5f - centre) * (y + 0.5f - centre));

                    circle[y * MaskSize + x] = (byte)(Math.Clamp(centre - d + 0.5f, 0f, 1f) * 255f);
                }
            }

            byte[] circleData = new byte[MaskSize * MaskSize];

            LayoutConverter.ConvertLinearToBlockLinear(circleData, MaskSize, MaskSize, MaskSize, 1, 1 << BlockHeightLog2, circle);

            // the banner before it is painted: nothing
            byte[] bannerData = new byte[(BannerWidth * 4 + 63) / 64 * 64 * ((BannerHeight + 127) / 128 * 128)];

            // the own banner of the user does not change while the applet runs: there from the start
            byte[] ownData = new byte[OwnBannerWidth * 4 * ((BannerHeight + 127) / 128 * 128)];

            if (ownBanner is { Length: OwnBannerWidth * BannerHeight * 4 })
            {
                LayoutConverter.ConvertLinearToBlockLinear(ownData, OwnBannerWidth, BannerHeight, OwnBannerWidth * 4, 4, 1 << BlockHeightLog2, ownBanner);
            }

            // red in the opacity of a white texture, as the applet's own masks; the banner's colours as they are
            List<(string, int, int, uint, uint, int, byte[])> added =
            [
                (MaskTexture, MaskSize, MaskSize, 0x0201u, 0x02010101u, BlockHeightLog2, maskData),
                (CircleTexture, MaskSize, MaskSize, 0x0201u, 0x02010101u, BlockHeightLog2, circleData),
                (OwnBannerTexture, OwnBannerWidth, BannerHeight, 0x0B01u, 0x05040302u, BlockHeightLog2, ownData),
            ];

            // the banner of a friend's page: only where something paints it (a megabyte of the applet's memory)
            if (friendBanner)
            {
                BannerMark.CopyTo(bannerData);

                added.Add((BannerTexture, BannerWidth, BannerHeight, 0x0B01u, 0x05040302u, BlockHeightLog2, bannerData));
            }

            return AddData(bntx, [.. added]);
        }

        // Textures of the archive become others, their pixels in room added at the end of the archive.
        private static byte[] AddData(byte[] bntx, (string Name, int Width, int Height, uint Format, uint Channels, int BlockHeightLog2, byte[] Data)[] textures)
        {
            const int Alignment = 0x1000;

            int nx = bntx.AsSpan().IndexOf("NX  "u8);
            int table = BinaryPrimitives.ReadInt32LittleEndian(bntx.AsSpan(0x18));
            int block = nx < 0 ? -1 : (int)BinaryPrimitives.ReadInt64LittleEndian(bntx.AsSpan(nx + 0x10));

            // the pixels end where the table of the pointers starts, the last thing of the file
            if (nx < 0 || !bntx.AsSpan(table, 4).SequenceEqual("_RLT"u8) || !bntx.AsSpan(block, 4).SequenceEqual("BRTD"u8) ||
                block + BinaryPrimitives.ReadInt64LittleEndian(bntx.AsSpan(block + 8)) != table || table % Alignment != 0 ||
                BinaryPrimitives.ReadInt32LittleEndian(bntx.AsSpan(table + 8)) != 2 ||
                BinaryPrimitives.ReadInt32LittleEndian(bntx.AsSpan(table + 0x10 + 0x18 + 8)) != block)
            {
                return null;
            }

            using MemoryStream added = new();
            int count = BinaryPrimitives.ReadInt32LittleEndian(bntx.AsSpan(nx + 4));
            int pointers = (int)BinaryPrimitives.ReadInt64LittleEndian(bntx.AsSpan(nx + 8));
            byte[] made = new byte[bntx.Length + textures.Sum(t => (t.Data.Length + Alignment - 1) / Alignment * Alignment)];

            bntx.AsSpan(0, table).CopyTo(made);

            foreach ((string name, int width, int height, uint format, uint channels, int blockHeightLog2, byte[] data) in textures)
            {
                int info = -1;

                for (int t = 0; t < count && info < 0; t++)
                {
                    int candidate = (int)BinaryPrimitives.ReadInt64LittleEndian(bntx.AsSpan(pointers + 8 * t));
                    int nameAt = (int)BinaryPrimitives.ReadInt64LittleEndian(bntx.AsSpan(candidate + 0x60));

                    if (Encoding.ASCII.GetString(bntx, nameAt + 2, BinaryPrimitives.ReadUInt16LittleEndian(bntx.AsSpan(nameAt))) == name)
                    {
                        info = candidate;
                    }
                }

                if (info < 0)
                {
                    return null;
                }

                int at = table + (int)added.Length;

                added.Write(data);
                added.Write(new byte[(Alignment - data.Length % Alignment) % Alignment]);

                BinaryPrimitives.WriteUInt16LittleEndian(made.AsSpan(info + 0x16), 1); // one level
                BinaryPrimitives.WriteUInt32LittleEndian(made.AsSpan(info + 0x1C), format);
                BinaryPrimitives.WriteInt32LittleEndian(made.AsSpan(info + 0x24), width);
                BinaryPrimitives.WriteInt32LittleEndian(made.AsSpan(info + 0x28), height);
                BinaryPrimitives.WriteInt32LittleEndian(made.AsSpan(info + 0x34), blockHeightLog2);
                BinaryPrimitives.WriteInt32LittleEndian(made.AsSpan(info + 0x50), data.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(made.AsSpan(info + 0x58), channels);
                BinaryPrimitives.WriteInt64LittleEndian(made.AsSpan((int)BinaryPrimitives.ReadInt64LittleEndian(bntx.AsSpan(info + 0x70))), at);
            }

            int more = (int)added.Length;

            added.ToArray().CopyTo(made, table);
            bntx.AsSpan(table).CopyTo(made.AsSpan(table + more));

            // the file, its pixels and the table that tells where they are: all that much longer
            BinaryPrimitives.WriteInt32LittleEndian(made.AsSpan(0x18), table + more);
            BinaryPrimitives.WriteInt32LittleEndian(made.AsSpan(0x1C), made.Length);
            BinaryPrimitives.WriteInt64LittleEndian(made.AsSpan(block + 8), table + more - block);
            BinaryPrimitives.WriteInt32LittleEndian(made.AsSpan(table + more + 4), table + more);
            BinaryPrimitives.WriteInt32LittleEndian(made.AsSpan(table + more + 0x10 + 0x18 + 0xC), table + more - block);

            return made;
        }
    }
}
