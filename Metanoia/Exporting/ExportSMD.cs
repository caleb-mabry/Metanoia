﻿using System.IO;
using Metanoia.Modeling;
using System.Text;
using System.Collections.Generic;

namespace Metanoia.Exporting
{
    public class ExportSMD : IModelExporter, IAnimationExporter
    {
        public static void Save(string FilePath, GenericModel Model)
        {
            new ExportSMD().Export(FilePath, Model);
        }

        public string Name()
        {
            return "Source Model";
        }

        public string Extension()
        {
            return ".smd";
        }

        public void Export(string FilePath, GenericModel Model)
        {
            using (StreamWriter w = new StreamWriter(new FileStream(FilePath, FileMode.Create)))
            {
                w.WriteLine("version 1");

                if(Model.Skeleton != null)
                {
                    w.WriteLine("nodes");
                    foreach(GenericBone bone in Model.Skeleton.Bones)
                    {
                        w.WriteLine($" {Model.Skeleton.IndexOf(bone)} \"{bone.Name}\" {bone.ParentIndex}");
                    }
                    // meshNodes
                    foreach (var mesh in Model.Meshes)
                    {
                        w.WriteLine($" {Model.Skeleton.Bones.Count + Model.Meshes.IndexOf(mesh)} \"{mesh.Name}\" -1");
                    }
                    w.WriteLine("end");
                    w.WriteLine("skeleton");
                    w.WriteLine("time 0");
                    foreach (GenericBone bone in Model.Skeleton.Bones)
                    {
                        w.WriteLine($" {Model.Skeleton.IndexOf(bone)} {bone.Position.X} {bone.Position.Y} {bone.Position.Z} {bone.Rotation.X} {bone.Rotation.Y} {bone.Rotation.Z}");
                    }
                    foreach (var mesh in Model.Meshes)
                    {
                        w.WriteLine($" {Model.Skeleton.Bones.Count + Model.Meshes.IndexOf(mesh)} 0 0 0 0 0 0");
                    }
                    w.WriteLine("end");
                }

                w.WriteLine("triangles");
                Dictionary<GenericTexture, string> TextureBank = new Dictionary<GenericTexture, string>();
                foreach (GenericMesh m in Model.Meshes)
                {
                    if (!m.Export)
                        continue;

                    var meshIndex = Model.Skeleton.Bones.Count + Model.Meshes.IndexOf(m);
                    m.MakeTriangles();
                    string MaterialName = m.Name;
                    if(m.MaterialName != null && Model.MaterialBank.ContainsKey(m.MaterialName))
                    {
                        var material = Model.MaterialBank[m.MaterialName];
                        if (material.TextureDiffuse != null && Model.TextureBank.ContainsKey(material.TextureDiffuse))
                        {
                            var texture = Model.TextureBank[material.TextureDiffuse];
                            if (TextureBank.ContainsKey(texture))
                            {
                                MaterialName = TextureBank[texture];
                            }
                            else
                            {
                                string TextureName = material.TextureDiffuse.Equals("") ? "Texture_" + TextureBank.Count + ".png" : material.TextureDiffuse + ".png";

                                if(texture.Mipmaps.Count != 0)
                                {
                                    Rendering.RenderTexture Temp = new Rendering.RenderTexture();
                                    Temp.LoadGenericTexture(texture);
                                    Temp.ExportPNG(new FileInfo(FilePath).Directory.FullName + "/" + TextureName);
                                    Temp.Delete();
                                }
                                TextureBank.Add(texture, TextureName);
                                MaterialName = TextureName;
                            }
                        }
                    }
                    for(int i = 0; i < m.Triangles.Count; i+=3)
                    {
                        w.WriteLine(MaterialName);
                        {
                            GenericVertex v = m.Vertices[(int)m.Triangles[i]];
                            w.WriteLine($" {meshIndex} {v.Pos.X} {v.Pos.Y} {v.Pos.Z} {v.Nrm.X} {v.Nrm.Y} {v.Nrm.Z} {v.UV0.X} {v.UV0.Y} " + WriteWeights(v));
                        }
                        {
                            GenericVertex v = m.Vertices[(int)m.Triangles[i+1]];
                            w.WriteLine($" {meshIndex} {v.Pos.X} {v.Pos.Y} {v.Pos.Z} {v.Nrm.X} {v.Nrm.Y} {v.Nrm.Z} {v.UV0.X} {v.UV0.Y} " + WriteWeights(v));
                        }
                        {
                            GenericVertex v = m.Vertices[(int)m.Triangles[i+2]];
                            w.WriteLine($" {meshIndex} {v.Pos.X} {v.Pos.Y} {v.Pos.Z} {v.Nrm.X} {v.Nrm.Y} {v.Nrm.Z} {v.UV0.X} {v.UV0.Y} " + WriteWeights(v));
                        }
                    }
                }
                w.WriteLine("end");

                w.Close();
            }

            if (Model.HasMorphs)
            {
                WriteVTA(FilePath.Replace(".smd", ".vta"), Model);
            }
        }

        private static string WriteWeights(GenericVertex v)
        {
            StringBuilder o = new StringBuilder();

            int Count = 0;

            if(v.Weights.X != 0)
            {
                Count++;
                o.Append($"{v.Bones.X} {v.Weights.X} ");
            }
            if (v.Weights.Y != 0)
            {
                Count++;
                o.Append($"{v.Bones.Y} {v.Weights.Y} ");
            }
            if (v.Weights.Z != 0)
            {
                Count++;
                o.Append($"{v.Bones.Z} {v.Weights.Z} ");
            }
            if (v.Weights.W != 0)
            {
                Count++;
                o.Append($"{v.Bones.W} {v.Weights.W} ");
            }


            return Count + " " + o.ToString();
        }


        private static void WriteVTA(string FilePath, GenericModel Model)
        {

            using (StreamWriter w = new StreamWriter(new FileStream(FilePath, FileMode.Create)))
            {
                w.WriteLine("version 1");

                if (Model.Skeleton != null)
                {
                    w.WriteLine("nodes");
                    foreach (GenericBone bone in Model.Skeleton.Bones)
                    {
                        w.WriteLine($" {Model.Skeleton.IndexOf(bone)} \"{bone.Name}\" {bone.ParentIndex}");
                    }
                    w.WriteLine("end");
                }

                // gather all morphs
                var basis = new Dictionary<GenericVertex, int>();
                var basisList = new List<GenericVertex>();
                var list = new Dictionary<string, List<MorphVertex>>();

                // combine everything into one pool...
                foreach(var mesh in Model.Meshes)
                {
                    foreach(var morph in mesh.Morphs)
                    {
                        if (!list.ContainsKey(morph.Name))
                            list.Add(morph.Name, new List<MorphVertex>());

                        var morphList = list[morph.Name];

                        foreach(var vertex in morph.Vertices)
                        {
                            var defaultVertex = mesh.Vertices[vertex.VertexIndex];
                            var morphVertex = vertex.Vertex;

                            if (!basis.ContainsKey(defaultVertex))
                            {
                                basis.Add(defaultVertex, basisList.Count);
                                basisList.Add(defaultVertex);
                            }

                            var morphIndex = basis[defaultVertex];

                            morphList.Add(new MorphVertex()
                            {
                                VertexIndex = morphIndex,
                                Vertex = morphVertex
                            });
                        }
                    }
                }

                w.WriteLine("skeleton");
                w.WriteLine("time 0 # basis shape key");

                int time = 1;
                foreach (var morphGroup in list)
                    w.WriteLine($"time {time++} # {morphGroup.Key}");

                w.WriteLine("end");

                w.WriteLine("vertexanimation");
                w.WriteLine("time 0 # basis shape key");
                for (int i = 0; i < basisList.Count; i++)
                {
                    var vertex = basisList[i];
                    w.WriteLine($"{i} {vertex.Pos.X} {vertex.Pos.Y} {vertex.Pos.Z} {vertex.Nrm.X} {vertex.Nrm.Y} {vertex.Nrm.Z}");
                }

                time = 1;
                foreach (var morphGroup in list)
                {
                    w.WriteLine($"time {time++} # {morphGroup.Key}");

                    var morphs = morphGroup.Value;

                    for (int i = 0; i < morphs.Count; i++)
                    {
                        var vertex = morphs[i].Vertex;
                        w.WriteLine($"{morphs[i].VertexIndex} {vertex.Pos.X} {vertex.Pos.Y} {vertex.Pos.Z} {vertex.Nrm.X} {vertex.Nrm.Y} {vertex.Nrm.Z}");
                    }
                }

                w.WriteLine("end");
            }
        }

        /// <summary>
        /// Export Animation
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="skeleton"></param>
        /// <param name="animation"></param>
        public void Export(string filePath, GenericSkeleton skeleton, GenericAnimation animation)
        {
            using (StreamWriter w = new StreamWriter(new FileStream(filePath, FileMode.Create)))
            {
                w.WriteLine("version 1");

                if (skeleton != null)
                {
                    w.WriteLine("nodes");
                    foreach (GenericBone bone in skeleton.Bones)
                    {
                        w.WriteLine($" {skeleton.IndexOf(bone)} \"{bone.Name}\" {bone.ParentIndex}");
                    }
                    w.WriteLine("end");
                    w.WriteLine("skeleton");

                    for(int i = 0; i < animation.FrameCount; i++)
                    {
                        animation.UpdateSkeleton(i, skeleton);
                        w.WriteLine("time " + i);
                        foreach (GenericBone bone in skeleton.Bones)
                        {
                            GenericBone b = new GenericBone();
                            b.Transform = bone.GetTransform(true);
                            w.WriteLine($" {skeleton.IndexOf(bone)} {b.Position.X} {b.Position.Y} {b.Position.Z} {b.Rotation.X} {b.Rotation.Y} {b.Rotation.Z}");
                        }
                    }
                    w.WriteLine("end");
                }
            }
        }
    }
}
