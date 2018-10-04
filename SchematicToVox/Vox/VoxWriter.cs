﻿using SchematicReader;
using SchematicToVox.Extensions;
using SchematicToVox.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SchematicToVox.Vox
{
    public class VoxWriter : VoxParser
    {
        private int _width = 0;
        private int _length = 0;
        private int _height = 0;
        private int _countSize = 0;

        private int _childrenChunkSize = 0;
        private Schematic _schematic;
        private Rotation _rotation = Rotation._PZ_PX_P;
        private Block[] _firstBlockInEachRegion;

        public bool WriteModel(string absolutePath, Schematic schematic)
        {
            _width = _length = _height = _countSize = 0;
            _schematic = schematic;
            using (var writer = new BinaryWriter(File.Open(absolutePath, FileMode.Create)))
            {
                writer.Write(Encoding.UTF8.GetBytes(HEADER));
                writer.Write(VERSION);
                writer.Write(Encoding.UTF8.GetBytes(MAIN));
                writer.Write(0); //MAIN CHUNK has a size of 0
                writer.Write(CountChildrenSize());
                WriteChunks(writer);
            }
            return true;
        }

        private int CountChildrenSize()
        {
            _width = (int)Math.Ceiling(((decimal)_schematic.Width / 126));
            _length = (int)Math.Ceiling(((decimal)_schematic.Length / 126));
            _height = (int)Math.Ceiling(((decimal)_schematic.Heigth / 126));
            _countSize = _width * _length * _height;

            int chunkSize = 24 * _countSize; //24 = 12 bytes for header and 12 bytes of content
            int chunkXYZI = (16 * _countSize) + _schematic.Blocks.Count() * 4; //16 = 12 bytes for header and 4 for the voxel count + (number of voxels) * 4
            int chunknTRNMain = 40; //40 = 
            int chunknGRP = 24 + _countSize * 4;
            int chunknTRN = 60 * _countSize;
            int chunknSHP = 32 * _countSize;

            GetFirstBlockForEachRegion();
            for (int i = 0; i < _countSize; i++)
            {
                string pos = GetWorldPosString(i);
                chunknTRN += Encoding.UTF8.GetByteCount(pos);
                chunknTRN += Encoding.UTF8.GetByteCount(Convert.ToString((byte)_rotation));
            }
            _childrenChunkSize = chunkSize; //SIZE CHUNK
            _childrenChunkSize += chunkXYZI; //XYZI CHUNK
            _childrenChunkSize += chunknTRNMain; //First nTRN CHUNK (constant)
            _childrenChunkSize += chunknGRP; //nGRP CHUNK
            _childrenChunkSize += chunknTRN; //nTRN CHUNK
            _childrenChunkSize += chunknSHP;

            return _childrenChunkSize;
        }

        private HashSet<Block> GetBlocksInRegion(Vector3 min, Vector3 max)
        {
            return _schematic.Blocks.Where(t => t.X >= min.x && t.Y >= min.y && t.Z >= min.z
            && t.X < max.x && t.Y < max.y && t.Z < max.z).ToHashSet();
        }


        private HashSet<Block> RecenterBlocks(HashSet<Block> blocks)
        {
            foreach (Block block in blocks)
            {
                block.X %= 126;
                block.Y %= 126;
                block.Z %= 126;
            }
            return blocks;
        }

        private void WriteChunks(BinaryWriter writer)
        {
            for (int i = 0; i < _countSize; i++)
            {
                WriteSizeChunk(writer);
                WriteXyziChunk(writer, i);
            }

            writer.Write(Encoding.UTF8.GetBytes(nTRN));
            writer.Write(28); //Main nTRN has always a 28 bytes size
            writer.Write(0); //Child nTRN chunk size
            writer.Write(0); // ID of nTRN
            writer.Write(0); //ReadDICT size for attributes (none)
            writer.Write(1); //Child ID
            writer.Write(-1); //Reserved ID
            writer.Write(-1); //Layer ID
            writer.Write(1); //Read Array Size
            writer.Write(0); //ReadDICT size


            WriteGroupChunk(writer);
            for (int i = 0; i < _countSize; i++)
            {
                WriteTransformChunk(writer, i);
                WriteShapeChunk(writer, i);
            }
        }

        private void WriteSizeChunk(BinaryWriter writer)
        {
            writer.Write(Encoding.UTF8.GetBytes(SIZE));
            writer.Write(12); //Chunk Size (constant)
            writer.Write(0); //Child Chunk Size (constant)

            writer.Write(126); //Width
            writer.Write(126); //Height
            writer.Write(126); //Depth
        }

        private void WriteXyziChunk(BinaryWriter writer, int index)
        {
            writer.Write(Encoding.UTF8.GetBytes(XYZI));
            HashSet<Block> blocks = new HashSet<Block>();
            if (_schematic.Blocks.Count > 0)
            {
                Block firstBlock = _firstBlockInEachRegion[index];
                blocks = GetBlocksInRegion(new Vector3(firstBlock.X, firstBlock.Y, firstBlock.Z), new Vector3(firstBlock.X + 126, firstBlock.Y + 126, firstBlock.Z + 126));
                blocks = RecenterBlocks(blocks);
            }
            Console.WriteLine(blocks.Count());
            writer.Write((blocks.Count() * 4) + 4); //XYZI chunk size
            writer.Write(0); //Child chunk size (constant)
            writer.Write(blocks.Count()); //Blocks count

            foreach (Block block in blocks)
            {
                writer.Write((byte)block.X);
                writer.Write((byte)block.Y);
                writer.Write((byte)block.Z);
                writer.Write((byte)79); //TODO: Apply color of the block
                _schematic.Blocks.Remove(block);
            }
        }

        private void WriteTransformChunk(BinaryWriter writer, int index)
        {
            writer.Write(Encoding.UTF8.GetBytes(nTRN));
            string pos = GetWorldPosString(index);
            writer.Write(48 + Encoding.UTF8.GetByteCount(pos)
                            + Encoding.UTF8.GetByteCount(Convert.ToString((byte)_rotation))); //nTRN chunk size
            writer.Write(0); //nTRN child chunk size
            writer.Write(2 * index + 2); //ID
            writer.Write(0); //ReadDICT size for attributes (none)
            writer.Write(2 * index + 3);//Child ID
            writer.Write(-1); //Reserved ID
            writer.Write(-1); //Layer ID
            writer.Write(1); //Read Array Size
            writer.Write(2); //Read DICT Size (previously 1)

            writer.Write(2); //Read STRING size
            writer.Write(Encoding.UTF8.GetBytes("_r"));
            writer.Write(Encoding.UTF8.GetByteCount(Convert.ToString((byte)_rotation)));
            writer.Write(Encoding.UTF8.GetBytes(Convert.ToString((byte)_rotation)));


            writer.Write(2); //Read STRING Size
            writer.Write(Encoding.UTF8.GetBytes("_t"));
            writer.Write(Encoding.UTF8.GetByteCount(pos));
            writer.Write(Encoding.UTF8.GetBytes(pos));
        }

        private void GetFirstBlockForEachRegion()
        {
            _firstBlockInEachRegion = new Block[_countSize];
            
            for (int i = 0; i < _countSize; i++)
            {
                int z = ((i % _width) * 126);
                int y = (((i / _width) % _height) * 126);
                int x = (i / (_width * _height) * 126);
                Block block = new Block(x, y, z, 0, 0, 0);
                _firstBlockInEachRegion[i] = block;
            }
        }

        private string GetWorldPosString(int index)
        {
            int worldPosX = _firstBlockInEachRegion[index].X - (_length / 2) * 126;
            int worldPosZ = _firstBlockInEachRegion[index].Z - (_width / 2) * 126;
            int worldPosY = _firstBlockInEachRegion[index].Y + 126;

            string pos = worldPosZ + " " + worldPosX + " " + worldPosY;
            return pos;
        }

        private void WriteShapeChunk(BinaryWriter writer, int index)
        {
            writer.Write(Encoding.UTF8.GetBytes(nSHP));
            writer.Write(20); //nSHP chunk size
            writer.Write(0); //nSHP child chunk size
            writer.Write(2 * index + 3); //ID
            writer.Write(0);
            writer.Write(1);
            writer.Write(index);
            writer.Write(0);
        }

        private void WriteGroupChunk(BinaryWriter writer)
        {
            writer.Write(Encoding.UTF8.GetBytes(nGRP));
            writer.Write(16 + (4 * (_countSize - 1))); //nGRP chunk size
            writer.Write(0); //Child nGRP chunk size
            writer.Write(1); //ID of nGRP
            writer.Write(0); //Read DICT size for attributes (none)
            writer.Write(_countSize);
            for (int i = 0; i < _countSize; i++)
            {
                writer.Write((2 * i) + 2); //id for childrens (start at 2, increment by 2)
            }
        }
    }
}
