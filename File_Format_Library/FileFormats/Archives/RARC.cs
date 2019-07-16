﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Toolbox;
using System.Windows.Forms;
using Toolbox.Library;
using Toolbox.Library.IO;

namespace FirstPlugin
{
    public class RARC : IArchiveFile, IFileFormat, IDirectoryContainer, IContextMenuNode
    {
        public FileType FileType { get; set; } = FileType.Archive;

        public bool CanSave { get; set; }
        public string[] Description { get; set; } = new string[] { "RARC" };
        public string[] Extension { get; set; } = new string[] { "*.rarc", "*.arc", "*.yaz0" };
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public IFileInfo IFileInfo { get; set; }

        public bool CanAddFiles { get; set; }
        public bool CanRenameFiles { get; set; }
        public bool CanReplaceFiles { get; set; }
        public bool CanDeleteFiles { get; set; }

        public bool Identify(System.IO.Stream stream)
        {
            using (var reader = new Toolbox.Library.IO.FileReader(stream, true))
            {
                return reader.CheckSignature(4, "RARC");
            }
        }

        public Type[] Types
        {
            get
            {
                List<Type> types = new List<Type>();
                return types.ToArray();
            }
        }

        public List<FileEntry> files = new List<FileEntry>();
        public List<INode> nodes = new List<INode>();

        public IEnumerable<ArchiveFileInfo> Files => files;
        public IEnumerable<INode> Nodes => nodes;

        public string Name
        {
            get { return FileName; }
            set { FileName = value; }
        }

        private DirectoryEntry[] Directories;

        private uint HeaderSize = 32;
        private uint Unknown = 256;

        public ToolStripItem[] GetContextMenuItems()
        {
            List<ToolStripItem> Items = new List<ToolStripItem>();
            Items.Add(new ToolStripMenuItem("Save", null, SaveAction, Keys.Control | Keys.S));
            return Items.ToArray();
        }

        private void SaveAction(object sender, EventArgs args)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = Utils.GetAllFilters(this);
            sfd.FileName = FileName;

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                STFileSaver.SaveFileFormat(this, sfd.FileName);
            }
        }

        public void Load(System.IO.Stream stream)
        {
            CanSave = true;

            using (var reader = new FileReader(stream))
            {
                reader.ByteOrder = Syroot.BinaryData.ByteOrder.BigEndian;
                reader.ReadSignature(4, "RARC");
                uint FileSize = reader.ReadUInt32();
                HeaderSize = reader.ReadUInt32();
                uint DataOffset = reader.ReadUInt32();
                uint FileDataSize = reader.ReadUInt32();
                uint EndOfFileOffset = reader.ReadUInt32();
                byte[] Padding = reader.ReadBytes(8);


                //Info Block
                long pos = reader.Position;

                uint DirectoryCount = reader.ReadUInt32();
                uint DirectoryOffset = reader.ReadUInt32() + (uint)pos;
                uint TotalNodeCount = reader.ReadUInt32();
                uint NodeOffset = reader.ReadUInt32() + (uint)pos;
                uint StringTableSize = reader.ReadUInt32();
                uint StringTablOffset = reader.ReadUInt32() + (uint)pos;
                ushort NodeCount = reader.ReadUInt16();
                Unknown = reader.ReadUInt16();
                byte[] Padding2 = reader.ReadBytes(4);

                Directories = new DirectoryEntry[DirectoryCount];
                for (int dir = 0; dir < DirectoryCount; dir++)
                    Directories[dir] = new DirectoryEntry(this);

                Console.WriteLine($"DirectoryCount {DirectoryCount}");
                Console.WriteLine($"StringTablOffset {StringTablOffset}");

                reader.SeekBegin(DirectoryOffset);
                for (int dir = 0; dir < DirectoryCount; dir++)
                {
                    Directories[dir].Read(reader);
                }

                for (int dir = 0; dir < DirectoryCount; dir++)
                {
                    uint NamePointer = StringTablOffset + Directories[dir].NameOffset;
                    Directories[dir].Name = ReadStringAtTable(reader, NamePointer);

                    for (int n = 0; n < Directories[dir].NodeCount; n++)
                    {
                        reader.SeekBegin(NodeOffset + ((n + Directories[dir].FirstNodeIndex) * 0x14));
                        FileEntry entry = new FileEntry();
                        entry.Read(reader);
                        NamePointer = StringTablOffset + entry.NameOffset;
                        entry.Name = ReadStringAtTable(reader, NamePointer);

                        //These make it crash so just skip them
                        //Unsure what purpose these have
                        if (entry.Name == "." || entry.Name == "..")
                            continue;

                        if (entry.IsDirectory)
                        {
                            Directories[dir].AddNode(Directories[entry.Offset]);
                        }
                        else
                        {
                            using (reader.TemporarySeek(pos + DataOffset + entry.Offset, System.IO.SeekOrigin.Begin))
                            {
                                entry.FileData = reader.ReadBytes((int)entry.Size);
                            }
                            entry.FileName = entry.Name;

                            Directories[dir].AddNode(entry);
                        }
                    }
                }

                this.Name = Directories[0].Name;
                nodes.AddRange(Directories[0].Nodes);
            }
        }

        public int GetTotalCount()
        {
            int count = 0;
            foreach (var dir in Nodes)
                GetTotalCount(dir, count);
            return count;
        }

        public int GetFileCount()
        {
            int count = 0;
            foreach (var dir in Nodes)
                GetFileCount(dir, count);
            return count;
        }

        public int GetDirectoryCount()
        {
            int count = 0;
            foreach (var dir in Nodes)
                GetDirectoryCount(dir, count);
            return count;
        }

        private int GetTotalCount(INode node, int count)
        {
            count++;

            if (node is DirectoryEntry)
            {
                foreach (var c in ((DirectoryEntry)node).nodes)
                    return GetTotalCount(c, count);
            }

            return count;
        }


        private int GetFileCount(INode node, int count)
        {
            if (node is FileEntry)
                count++;

            if (node is DirectoryEntry)
            {
                foreach (var c in ((DirectoryEntry)node).nodes)
                    return GetFileCount(c, count);
            }

            return count;
        }

        private int GetDirectoryCount(INode node, int count)
        {
            if (node is DirectoryEntry)
            {
                count++;

                foreach (var c in ((DirectoryEntry)node).nodes)
                    return GetDirectoryCount(c, count);
            }

            return count;
        }

        public void SaveFile(FileWriter writer)
        {
            long pos = writer.Position;

            writer.ByteOrder = Syroot.BinaryData.ByteOrder.BigEndian;
            writer.WriteSignature("RARC");
            writer.Write(uint.MaxValue); //FileSize
            writer.Write(HeaderSize);
            writer.Write(uint.MaxValue); //DataOffset
            writer.Write(uint.MaxValue); //File Size
            writer.Write(uint.MaxValue); //End of file
            writer.Seek(8); //padding

            writer.SeekBegin(HeaderSize);
            long InfoPos = writer.Position;

            writer.Write(GetDirectoryCount()); 
            writer.Write(uint.MaxValue); //DirectoryOffset
            writer.Write(GetFileCount());
            writer.Write(uint.MaxValue); //File Node Offset
            writer.Write(uint.MaxValue); //String pool size
            writer.Write(uint.MaxValue); //String pool offset
            writer.Write((ushort)GetFileCount());
            writer.Write((ushort)Unknown);
            writer.Write(0); //padding

            //Write directory Offset
            WriteOffset(writer, 4, InfoPos);
            for (int dir = 0; dir < Directories.Length; dir++)
            {
                Directories[dir].Write(writer);
            }

            writer.Seek(16); //Add padding after directories

            //Write the node offset
            WriteOffset(writer, 12, InfoPos);
            for (int dir = 0; dir < Directories.Length; dir++)
            {
                for (int n = 0; n < Directories[dir].NodeCount; n++)
                {

                }
            }

            //Write file size
            using (writer.TemporarySeek(pos + 0x4, System.IO.SeekOrigin.Begin))
            {
                writer.Write((uint)writer.BaseStream.Length);
            }
        }

        private void WriteDirectories()
        {

        }

        private void WriteOffset(FileWriter writer, long Target, long RelativePosition)
        {
            long Position = writer.Position;
            using (writer.TemporarySeek(RelativePosition + Target, System.IO.SeekOrigin.Begin))
            {
                writer.Write((uint)(Position - RelativePosition));
            }
        }

        private string ReadStringAtTable(FileReader reader,  uint NameOffset)
        {
            using (reader.TemporarySeek(NameOffset, System.IO.SeekOrigin.Begin))
            {
                return reader.ReadZeroTerminatedString();
            }
        }

        private static ushort CalculateHash(string Name)
        {
            ushort Hash = 0;
            for (int i = 0; i < Name.Length; i++)
            {
                Hash *= 3;
                Hash += Name[i];
            }
            return Hash;
        }

        private void CreateDirectoryEntry()
        {
            
        }

        public class DirectoryEntry : IDirectoryContainer
        {
            public RARC ParentArchive { get; }

            public string Name { get; set; }

            private uint Identifier;

            internal uint NameOffset; //Relative to string table

            public ushort Hash { get; set; }

            public ushort NodeCount;

            public uint FirstNodeIndex { get; set; }

            public DirectoryEntry(RARC rarc) { ParentArchive = rarc; }

            public IEnumerable<INode> Nodes { get { return nodes; } }
            public List<INode> nodes = new List<INode>();

            public void AddNode(INode node)
            {
                nodes.Add(node);
            }


            internal long _positionPtr;
            public void Read(FileReader reader)
            {
                Identifier = reader.ReadUInt32();
                NameOffset = reader.ReadUInt32();
                Hash = reader.ReadUInt16();
                NodeCount = reader.ReadUInt16();
                FirstNodeIndex = reader.ReadUInt32();
            }

            public void Write(FileWriter writer)
            {
                _positionPtr = writer.Position;

                Hash = CalculateHash(Name);

                writer.Write(Identifier);
                writer.Write(NameOffset);
                writer.Write(Hash);
                writer.Write(NodeCount);
                writer.Write(FirstNodeIndex);
            }
        }

        public void Unload()
        {

        }

        public byte[] Save()
        {
            var mem = new System.IO.MemoryStream();
            SaveFile(new FileWriter(mem));
            return mem.ToArray();
        }

        public bool AddFile(ArchiveFileInfo archiveFileInfo)
        {
            return false;
        }

        public bool DeleteFile(ArchiveFileInfo archiveFileInfo)
        {
            return false;
        }

        public class FileEntry : ArchiveFileInfo
        {
            //According to this to determine directory or not 
            //https://github.com/LordNed/WArchive-Tools/blob/3c7fdefe54b4c7634a042847b7455de61705033f/ArchiveToolsLib/Archive/Archive.cs#L44
            public bool IsDirectory { get { return (Flags & 2) >> 1 == 1; } }

            public ushort FileId { get; set; }
            public ushort Hash { get; set; }
            public byte Flags { get; set; }

            internal uint Size;
            internal uint Offset;
            internal ushort NameOffset;

            internal long _positionPtr;
            public void Read(FileReader reader)
            {
                FileId = reader.ReadUInt16();
                Hash = reader.ReadUInt16();
                Flags = reader.ReadByte();
                reader.Seek(1); //Padding
                NameOffset = reader.ReadUInt16();
                Offset = reader.ReadUInt32();
                Size = reader.ReadUInt32();
            }

            public void Write(FileWriter writer)
            {
                _positionPtr = writer.Position;

                SaveFileFormat();

                writer.Write(FileId);
                writer.Write(Hash);
                writer.Write(Flags);
                writer.Seek(1); //Padding
                writer.Write(NameOffset);
                writer.Write(uint.MaxValue);
                writer.Write(FileData.Length);
            }
        }
    }
}