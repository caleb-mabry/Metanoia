﻿using System;
using System.Collections.Generic;
using Metanoia.Modeling;
using System.IO;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace Metanoia.Formats.N64
{
    public class PS_Fragment : I3DModelFormat
    {
        GenericModel Model = new GenericModel();

        public string Name => "Pokemon Stadium Model";
        public string Extension => ".pers";
        public string Description => "";
        public bool CanOpen => true;
        public bool CanSave => false;

        public void Save(string filePath)
        {
            throw new NotImplementedException();
        }

        public bool Verify(FileItem file)
        {
            return file.Extension.Equals(Extension);
        }

        public PS_Fragment()
        {

        }

        public void Open(FileItem File)
        {
            var Data = File.GetFileBinary();

            Model = new GenericModel();
            Model.Skeleton = new GenericSkeleton();
            Model.Skeleton.RotationOrder = RotationOrder.ZYX;
            
            // decompress
            using (DataReader reader = new DataReader(new MemoryStream(Data)))
            {
                reader.BigEndian = true;

                if (reader.BaseStream.Length < 8 || !reader.ReadString(8).Equals("PERS-SZP"))
                {
                    // not compressed
                }
                else
                {
                    reader.BigEndian = true;
                    uint compressedoffset = reader.ReadUInt32();

                    reader.Seek(compressedoffset);
                    Data = Tools.Decompress.YAY0(reader.ReadBytes((int)(reader.BaseStream.Length - compressedoffset)));
                }
            }

            // decode file contents
            using (DataReader reader = new DataReader(new MemoryStream(Data)))
            {
                reader.BigEndian = true;
                reader.Seek(0x8);
                if (!reader.ReadString(8).Equals("FRAGMENT")) return;
                reader.Seek(0x10);
                uint DataOffset = reader.ReadUInt32();
                uint RelocationTableOffset = reader.ReadUInt32();
                reader.Skip(4); // filesize
                reader.Skip(4); // tableOffset again

                // Relocation Table
                reader.Seek(RelocationTableOffset);
                int entryCount = reader.ReadInt32();
                int[] offsets = new int[entryCount];
                for (int i = 0; i < entryCount; i++)
                {
                    int mask = 0xFFFF;
                    offsets[i] = reader.ReadInt32();
                    if (RelocationTableOffset > 0xFFFF) mask = 0x1FFFF; // hack
                    uint temp = reader.Position;
                    reader.Seek((uint)(offsets[i] & 0x1FFFF));
                    reader.WriteInt32At(reader.ReadInt32() & mask, offsets[i] & 0x1FFFF);
                    reader.Seek((uint)(offsets[i] & 0x1FFFF));
                    reader.Seek(temp);
                }

                // main data
                reader.Seek(DataOffset);
                reader.Skip(0x10);
                uint mainOffset = reader.ReadUInt32();

                // main stuff
                reader.Seek(mainOffset);
                reader.Skip(8); // i dunno
                uint offsetToTextureOffset = reader.ReadUInt32();

                reader.Seek(offsetToTextureOffset);
                uint textureOffset = reader.ReadUInt32();

                // may be objects instead of just textures
                reader.Seek(textureOffset);
                int TextureCount = reader.ReadInt32() & 0xFFFF; // should have 0x17 in front
                int PaletteCount = reader.ReadInt16();
                int VertexCount = reader.ReadInt16();
                uint TextureOffset = reader.ReadUInt32();
                uint PaletteOffset = reader.ReadUInt32();
                uint VertexOffset = reader.ReadUInt32();
                reader.Skip(0x1C); //I dunno
                uint objectOffset = reader.ReadUInt32();


                // Textures-------------------------------------------------------
                List<GenericTexture> Textures = new List<GenericTexture>();
                List<byte[]> Palettes = new List<byte[]>();

                //Read Palettes
                reader.Seek(PaletteOffset);
                for (int i = 0; i < PaletteCount; i++)
                {
                    int colorcount = reader.ReadInt32();
                    uint OffsetData = reader.ReadUInt32();
                    int OffsetSomething = reader.ReadInt32();
                    Palettes.Add(reader.GetSection(OffsetData, colorcount * 2));
                }

                // Read Textures?
                reader.Seek(TextureOffset);
                //Console.WriteLine(reader.pos().ToString("x"));
                for (int i = 0; i < TextureCount; i++)
                {
                    int format = reader.ReadByte();
                    int bitsize = reader.ReadByte();
                    uint width = reader.ReadUInt16();
                    uint height = reader.ReadUInt16();
                    uint size = reader.ReadUInt16(); // sometimes 8 maybe an id? pointer?
                    uint texDataOffset = reader.ReadUInt32() & 0x1FFFF;

                    Console.WriteLine("Texture " + format + " " + bitsize + " " + size + " " + width + " " + height);

                    GenericTexture tex = new GenericTexture();
                    Textures.Add(tex);
                    tex.Name = "Texture_" + i;
                    byte[] data;
                    tex.Width = width;
                    tex.Height = height;

                    tex.PixelFormat = PixelFormat.Rgba;
                    if (format == 4)
                    {
                        // Raw
                        if (bitsize == 1) //RGBA
                        {
                            data = reader.GetSection(texDataOffset, (int)size * bitsize);

                            tex.Mipmaps.Add(data);
                            tex.InternalFormat = PixelInternalFormat.Luminance8;
                            tex.PixelFormat = PixelFormat.Luminance;
                        }
                    }
                    else
                   if (format == 2)
                    {
                        // Paletted

                        if (bitsize == 0) //4bpp
                        {
                            data = reader.GetSection(texDataOffset, (int)size / 2);

                            tex.Mipmaps.Add(data);
                            tex.InternalFormat = PixelInternalFormat.Alpha4;
                        }
                        else if (bitsize == 1) //8bpp
                        {
                            data = reader.GetSection(texDataOffset, (int)size * bitsize);

                            tex.Mipmaps.Add(data);
                            tex.InternalFormat = PixelInternalFormat.Alpha8;
                        }
                    }
                    else
                    {
                        if (bitsize == 2)
                        {
                            data = reader.GetSection(texDataOffset, (int)size * bitsize);
                            // swap endian
                            for (int j = 0; j < data.Length / 2; j++)
                            {
                                byte temp = data[j * 2];
                                data[j * 2] = data[(j * 2) + 1];
                                data[(j * 2) + 1] = temp;
                            }
                            tex.Mipmaps.Add(data);
                            tex.InternalFormat = PixelInternalFormat.Rgb5A1;
                        }
                        else if (bitsize == 3)
                        {
                            tex.InternalFormat = PixelInternalFormat.Rgba8;
                            data = reader.GetSection(texDataOffset, (int)size * 4);
                            tex.Mipmaps.Add(data);
                        }
                    }
                }

                // Objects--------------------------------------------------------
                // now parse until end
                bool done = false;
                Stack<GenericBone> boneStack = new Stack<GenericBone>();
                GenericBone parentBone = null;

                reader.Seek(objectOffset);
                reader.Skip(4); // idk
                int maybeCount = reader.ReadInt32();

                string currentMaterialName = "";
                GenericMaterial currentMaterial = new GenericMaterial();
                GenericBone CurrentBone = null;

                while (!done)
                {
                    int doff, temp;
                    //Console.WriteLine(reader.Position().ToString("x") + " " + reader.ReadByte().ToString("x"));
                    reader.ReadByte();
                    reader.Seek(reader.Position - 1);
                    switch (reader.ReadByte())
                    {
                        case 0x03: // Perhaps some object offset? Offset goes to beginning of file. Material maybe?
                            reader.Skip(0x08 - 1);
                            break;
                        case 0x05:
                            reader.Skip(0x04 - 1);
                            boneStack.Push(CurrentBone);
                            parentBone = boneStack.Peek();
                            break;
                        case 0x06:
                            reader.Skip(0x04 - 1);
                            if (boneStack.Count > 0)
                            {
                                boneStack.Pop();
                                if (boneStack.Count > 0)
                                    parentBone = boneStack.Peek();
                            }
                            break;
                        case 0x08:
                            reader.Skip(4 - 1);
                            int s1 = reader.ReadByte() & 0x7F;
                            reader.Skip(1);
                            int s2 = reader.ReadInt16();
                            reader.Skip(4);
                            // pops matrix
                            // maybe pop until you get to this bone?
                            //Also maybe some texture thing?
                            Console.WriteLine("What dis?" + " " + s1 + " " + s2);
                            //throw new Exception("Weird Special Thing");
                            /*for (int i = popto - s1; i < popto + s2; i++)
                            {
                                Bone bo = skel.GetBoneByID(i);
                                boneStack.Push(bo);
                            }*/
                            break;
                        case 0x18: // idk has 4 values
                            reader.Skip(0x08 - 1);
                            break;
                        case 0x1D:
                            int id = reader.ReadByte();
                            int what = reader.ReadByte();
                            int parent = (sbyte)reader.ReadByte();

                            // read bone properties
                            Vector3 trans = new Vector3((short)reader.ReadInt16(), (short)reader.ReadInt16(), (short)reader.ReadInt16());
                            Vector3 rot = new Vector3((short)reader.ReadInt16() / 180, ((short)reader.ReadInt16()) / 180, (short)reader.ReadInt16() / 180);

                            // to radians
                            rot.X = (rot.X * (float)Math.PI) / 180f;
                            rot.Y = (rot.Y * (float)Math.PI) / 180f;
                            rot.Z = (rot.Z * (float)Math.PI) / 180f;

                            //rot = new Vector3(rot.Z, rot.X, rot.Y);

                            Vector3 scale = new Vector3();
                            scale.X = reader.ReadInt16() + reader.ReadInt16() / (float)0xFFFF;
                            scale.Y = reader.ReadInt16() + reader.ReadInt16() / (float)0xFFFF;
                            scale.Z = reader.ReadInt16() + reader.ReadInt16() / (float)0xFFFF;

                            int parent2 = boneStack.Count;

                            GenericBone b = new GenericBone();
                            b.Name = "Bone_" + id;
                            if (parentBone != null)
                                b.ParentIndex = Model.Skeleton.IndexOf(parentBone);
                            else
                                b.ParentIndex = -1;
                            b.ID = id;
                            b.Scale = new Vector3(1, 1, 1); // scale
                            b.Position = trans;
                            b.Rotation = rot;

                            CurrentBone = b;
                            Model.Skeleton.Bones.Add(b);

                            //Console.WriteLine(reader.Position().ToString("x") + " " + b.Name + " " + b.p1 + " " + what + " " + parent + " " + boneStack.Count + " " + trans.ToString() + " " + rot.ToString() + " " + scale.ToString());
                            //Console.WriteLine(b.transform.ToString());
                            break;
                        case 0x1E:
                            //reader.Skip(3);
                            reader.Skip(1);
                            int w = reader.ReadInt16(); // bone index
                            doff = reader.ReadInt32();
                            temp = (int)reader.Position;

                            reader.Seek((uint)doff);
                            {
                                GenericMesh mesh = DisplayListToGenericMesh(N64Tools.ReadDisplayList(reader, Model.Skeleton.IndexOf(Model.Skeleton.GetBoneByID(w)), Model.Skeleton.GetWorldTransform(Model.Skeleton.GetBoneByID(w))));
                                mesh.MaterialName = currentMaterialName;
                                Model.Meshes.Add(mesh);
                            }
                            reader.Seek((uint)temp);

                            break;
                        case 0x22:
                            int materialIndex = reader.ReadByte();
                            int ww = (short)reader.ReadInt16();

                            doff = reader.ReadInt32();
                            temp = (int)reader.Position;
                            if (doff == 0) continue;

                            reader.Seek((uint)doff);
                            {
                                GenericMesh mesh = DisplayListToGenericMesh(N64Tools.ReadDisplayList(reader, Model.Skeleton.IndexOf(parentBone), Model.Skeleton.GetWorldTransform(parentBone)));
                                mesh.MaterialName = currentMaterialName;
                                Model.Meshes.Add(mesh);
                            }
                            reader.Seek((uint)temp);
                            //((DisplayList)DisplayLists.Nodes[DisplayLists.Nodes.Count - 1]).Mat = unk;
                            //Console.WriteLine("Material Maybe 0x" + reader.pos().ToString("x") + " " + unk + " " + .Count);
                            break;
                        case 0x23:
                            reader.Skip(1);
                            int tidunk = (short)reader.ReadInt16();
                            int texOff = reader.ReadInt32();//& 0x1FFFF Material Offset?
                            int tid = (short)reader.ReadInt16();
                            int pid = (short)reader.ReadInt16();
                            reader.Skip(4);// 0xFF padding
                            //Console.WriteLine("TextureCount " + tid + " " + pid + " " + tidunk + " " + texOfreader.ToString("x"));
                            if (tid != -1)
                            {
                                currentMaterial = new GenericMaterial();
                                currentMaterialName = "material_" + Model.MaterialBank.Count;
                                var diffuse = BakeTexturePalette(Textures[tid], pid, Palettes);
                                diffuse.Name = "texture_" + Model.TextureBank.Count;
                                currentMaterial.TextureDiffuse = diffuse.Name;
                                Model.TextureBank.Add(diffuse.Name, diffuse);
                                Model.MaterialBank.Add(currentMaterialName, currentMaterial);
                            }
                            else
                            {
                                var currentTexture = currentMaterial.TextureDiffuse;
                                currentMaterial = new GenericMaterial();
                                currentMaterialName = "material_" + Model.MaterialBank.Count;
                                currentMaterial.TextureDiffuse = currentTexture;
                                Model.MaterialBank.Add(currentMaterialName, currentMaterial);
                            }

                            // Read Texture Info At Offset
                            int tt = (int)reader.Position;
                            reader.Seek((uint)texOff);
                            ReadTextureCodes(reader, currentMaterial);
                            reader.Seek((uint)tt);
                            break; // Texture Binding
                        case 0x24: reader.Skip(3); break; // has to do with matrix popping
                        case 0x25:
                            reader.Skip(0x04 - 1);
                            //Console.WriteLine("Unknown 0x" + reader.pos().ToString("x")); 
                            break;
                        default:
                            done = true;
                            break;
                    };
                }

            }


            // Post Process

            // optimized texture sharing
            Dictionary<string, string> newTexName = new Dictionary<string, string>();
            Dictionary<string, GenericTexture> newTexBank = new Dictionary<string, GenericTexture>();
            Dictionary<byte[], string> TextureBank = new Dictionary<byte[], string>();

            foreach (var tex in Model.TextureBank)
            {
                if (!TextureBank.ContainsKey(tex.Value.Mipmaps[0]))
                {
                    string newName = "texture_" + newTexName.Count;
                    newTexName.Add(tex.Key, newName);
                    newTexBank.Add(newName, tex.Value);
                    TextureBank.Add(tex.Value.Mipmaps[0], newName);
                }
                else
                {
                    newTexName.Add(tex.Key, TextureBank[tex.Value.Mipmaps[0]]);
                }
            }

            Model.TextureBank = newTexBank;

            foreach(var mesh in Model.Meshes)
            {
                var material = Model.GetMaterial(mesh);
                if (material != null && material.TextureDiffuse != null)
                    material.TextureDiffuse = newTexName[material.TextureDiffuse];
            }

            Console.WriteLine(TextureBank.Count + " total textures");

            // Transform Verts
            /*int meshindex = 0;
            foreach(GenericMesh mesh in Model.Meshes)
            {
                mesh.Name = "Mesh_" + meshindex;
                GenericVertex[] CorrectedVertices = mesh.Vertices.ToArray();
                for(int i =0; i < CorrectedVertices.Length; i++)
                {
                    CorrectedVertices[i].Pos = Vector3.TransformPosition(CorrectedVertices[i].Pos, Model.Skeleton.GetWorldTransform((int)CorrectedVertices[i].Bones.X));
                }
                mesh.Vertices.Clear();
                mesh.Vertices.AddRange(CorrectedVertices);
            }*/

        }

        private void ReadTextureCodes(DataReader reader, GenericMaterial Mat)
        {
            bool done = false;
            while (!done)
            {
                //Console.WriteLine(reader.pos().ToString("x") + " " + reader.readByte().ToString("x"));
                //reader.Seek(reader.pos() - 1);
                switch (reader.ReadByte())
                {
                    case 0xF5:
                        reader.Seek(reader.Position - 1);
                        uint b1 = (uint)reader.ReadInt32();
                        uint b2 = (uint)reader.ReadInt32();

                        int cmT = (int)((b2 >> 18) & 0x3);
                        int cmS = (int)((b2 >> 8) & 0x3);

                        Console.WriteLine(b1.ToString("x") + " " + b2.ToString("x") + " " + cmT + " " + cmS);

                        switch (cmS)
                        {
                            case 0: Mat.TWrap = TextureWrapMode.Repeat; break;
                            case 1: Mat.TWrap = TextureWrapMode.MirroredRepeat; break;
                            case 2: Mat.TWrap = TextureWrapMode.ClampToEdge; break;
                            case 3: Mat.TWrap = TextureWrapMode.ClampToEdgeSgis; break;
                        }
                        switch (cmT)
                        {
                            case 0: Mat.SWrap = TextureWrapMode.Repeat; break;
                            case 1: Mat.SWrap = TextureWrapMode.MirroredRepeat; break;
                            case 2: Mat.SWrap = TextureWrapMode.ClampToEdge; break;
                            case 3: Mat.SWrap = TextureWrapMode.ClampToEdgeSgis; break;
                        }
                        //Mat.T = TextureWrapMode.Repeat;
                        //Mat.S = TextureWrapMode.Repeat;
                        break;
                    case 0xDF:
                        done = true;
                        break;
                    default:
                        reader.Skip(7);
                        break;
                }
            }
        }

        #region Tools

        public GenericMesh DisplayListToGenericMesh(N64DisplayList DL)
        {
            GenericMesh mesh = new GenericMesh();

            mesh.Name = "Mesh" + DL.faces.Count;

            foreach (int f in DL.faces)
            {
                mesh.Triangles.Add((uint)f);
            }

            foreach (N64Vertex_Normal nv in DL.verts)
            {
                mesh.Vertices.Add(new GenericVertex()
                {
                    Pos = nv.pos,
                    Nrm = nv.color.Xyz,
                    UV0 = nv.uv,
                    Bones = new Vector4(nv.bone, 0, 0, 0),
                    Weights = new Vector4(1, 0, 0, 0)
                });
            }

            return mesh;
        }

        public GenericTexture BakeTexturePalette(GenericTexture t, int pid, List<byte[]> Palette)
        {
            if (pid == -1)
                return t;

            GenericTexture tex = new GenericTexture();

            tex.Width = t.Width;
            tex.Height = t.Height;

            tex.Id = t.Id;
            tex.InternalFormat = PixelInternalFormat.Rgb5A1;
            tex.PixelFormat = PixelFormat.Rgba;

            byte[] palette = Palette[pid];
            byte[] Data = new byte[tex.Width * tex.Height * 2];

            for (int i = 0; i < t.Mipmaps[0].Length; i++)
            {
                if (palette.Length > 32)
                {
                    //8bpp
                    Data[i * 2 + 0] = palette[t.Mipmaps[0][i] * 2 + 1];
                    Data[i * 2 + 1] = palette[t.Mipmaps[0][i] * 2];
                }
                else
                {
                    //4bpp
                    int id = t.Mipmaps[0][i];
                    Data[i * 4 + 2] = palette[(id & 0xF) * 2 + 1];
                    Data[i * 4 + 3] = palette[(id & 0xF) * 2];

                    Data[i * 4 + 0] = palette[((id >> 4) & 0xF) * 2 + 1];
                    Data[i * 4 + 1] = palette[((id >> 4) & 0xF) * 2];

                }
            }

            tex.Mipmaps.Add(Data);

            return tex;
        }

        #endregion

        public GenericModel ToGenericModel()
        {
            return Model;
        }
    }
}
