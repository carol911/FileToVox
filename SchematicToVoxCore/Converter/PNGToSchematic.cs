﻿using SchematicToVoxCore.Extensions;
using SchematicToVoxCore.Schematics;
using SchematicToVoxCore.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace SchematicToVoxCore.Converter
{
    public static class PNGToSchematic
    {
        private static bool _excavate;
        private static int _height;
        private static bool _color;
        private static bool _top;
        private static Color[,] _mainColors;
        private static Color[,] _grayColors;
        private static Color[,] _fileColors;

        public static Schematic WriteSchematic(string path, string colorPath, int height, bool excavate, bool color, bool top)
        {
            _excavate = excavate;
            _height = height;
            _color = color;
            _top = top;
            return WriteSchematicFromImage(path, colorPath);
        }

        private static Schematic WriteSchematicFromImage(string path, string colorPath)
        {
            FileInfo info = new FileInfo(path);
            Bitmap bitmap = new Bitmap(info.FullName);

            if (colorPath != null)
            {
                FileInfo infoColor = new FileInfo(colorPath);
                Bitmap bitmapColor = new Bitmap(infoColor.FullName);
                if (bitmap.Height != bitmapColor.Height || bitmap.Width != bitmapColor.Width)
                {
                    throw new ArgumentException("[ERROR] Image color is not the same size of the original image");
                }
                _fileColors = GetColors(bitmapColor);
            }

            Bitmap grayScale = MakeGrayscale3(bitmap);

            _mainColors = GetColors(bitmap);
            _grayColors = GetColors(grayScale);

            if (bitmap.Width > 2016 || bitmap.Height > 2016)
            {
                throw new Exception("Image is too big (max size 2016x2016 px)");
            }

            Schematic schematic = new Schematic
            {
                Width = (short)bitmap.Width,
                Length = (short)bitmap.Height,
                Heigth = (short)_height,
                Blocks = new HashSet<Block>()
            };
            SchematicReader.LengthSchematic = schematic.Length;
            SchematicReader.WidthSchematic = schematic.Width;
            SchematicReader.HeightSchematic = schematic.Heigth;


            using (ProgressBar progressbar = new ProgressBar())
            {
                Console.WriteLine("[LOG] Started to write schematic from picture...");
                Console.WriteLine("[INFO] Picture Width: " + schematic.Width);
                Console.WriteLine("[INFO] Picture Length: " + schematic.Length);

                int size = schematic.Width * schematic.Length;
                for (int i = 0; i < size; i++)
                {
                    int x = i % schematic.Width;
                    int y = i / schematic.Width;
                    Color color = _mainColors[y, x];
                    Color colorGray = _grayColors[y, x];
                    Color finalColor = (colorPath != null) ? _fileColors[y, x] : (_color) ? color : colorGray;
                    if (color.A != 0)
                    {
                        if (_height != 1)
                        {
                            int height = GetHeight(colorGray);

                            if (_excavate)
                            {
                                GenerateFromMinNeighbor(ref schematic, finalColor, x, y);
                            }
                            else
                            {
                                if (_top)
                                {
                                    Block block = new Block((short)x, (short)(height - 1), (short)y, finalColor.ColorToUInt());
                                    AddBlock(ref schematic, block);
                                }
                                else
                                {
                                    AddMultipleBlocks(ref schematic, 0, height, x, y, finalColor);
                                }
                            }
                        }
                        else
                        {
                            Block block = new Block((short)x, (short)1, (short)y, color.ColorToUInt());
                            AddBlock(ref schematic, block);
                        }
                    }
                    progressbar.Report((i / (float)size));
                }
            }

            _mainColors = null;
            _fileColors = null;
            _grayColors = null;
            Console.WriteLine("[LOG] Done.");
            return schematic;
        }

        private static Bitmap MakeGrayscale3(Bitmap original)
        {
            //create a blank bitmap the same size as original
            Bitmap newBitmap = new Bitmap(original.Width, original.Height);

            //get a graphics object from the new image
            Graphics g = Graphics.FromImage(newBitmap);

            //create the grayscale ColorMatrix
            ColorMatrix colorMatrix = new ColorMatrix(
               new float[][]
               {
                 new float[] {.3f, .3f, .3f, 0, 0},
                 new float[] {.59f, .59f, .59f, 0, 0},
                 new float[] {.11f, .11f, .11f, 0, 0},
                 new float[] {0, 0, 0, 1, 0},
                 new float[] {0, 0, 0, 0, 1}
               });

            //create some image attributes
            ImageAttributes attributes = new ImageAttributes();

            //set the color matrix attribute
            attributes.SetColorMatrix(colorMatrix);

            //draw the original image on the new image
            //using the grayscale color matrix
            g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
               0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);

            //dispose the Graphics object
            g.Dispose();
            return newBitmap;
        }

        private static Color[,] GetColors(Bitmap bitmap)
        {
            Color[,] colors = new Color[bitmap.Height, bitmap.Width];
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    colors[y, x] = bitmap.GetPixel(x, y);
                }
            }
            return colors;
        }

        private static void AddMultipleBlocks(ref Schematic schematic, int minZ, int maxZ, int x, int y, Color color)
        {
            for (int z = minZ; z < maxZ; z++)
            {
                Block block = new Block((short)x, (short)z, (short)y, color.ColorToUInt());
                AddBlock(ref schematic, block);
            }
        }

        private static void AddBlock(ref Schematic schematic, Block block)
        {
            schematic.Blocks.Add(block);
        }

        private static int GetHeight(Color color)
        {
            int intensity = color.R + color.G + color.B;
            float position = intensity / (float)765;
            return (int)(position * _height);
        }

        private static void GenerateFromMinNeighbor(ref Schematic schematic, Color color, int x, int y)
        {
            int height = GetHeight(color);

            if (x - 1 > 0 && x + 1 < schematic.Width && y - 1 > 0 && y + 1 < schematic.Length)
            {
                var colorLeft = _grayColors[x - 1, y];
                var colorTop = _grayColors[x, y - 1];
                var colorRight = _grayColors[x + 1, y];
                var colorBottom = _grayColors[x, y + 1];

                int heightLeft = GetHeight(colorLeft);
                int heightTop = GetHeight(colorTop);
                int heightRight = GetHeight(colorRight);
                int heightBottom = GetHeight(colorBottom);

                var list = new List<int>
                {
                    heightLeft, heightTop, heightRight, heightBottom
                };

                int min = list.Min();
                if (min < height)
                {
                    AddMultipleBlocks(ref schematic, list.Min(), height, x, y, color);
                }
                else
                {
                    Block block = new Block((short)x, (short)(height - 1), (short)y, color.ColorToUInt());
                    AddBlock(ref schematic, block);
                }

            }
            else
            {
                AddMultipleBlocks(ref schematic, 0, height, x, y, color);

            }
        }
    }
}
