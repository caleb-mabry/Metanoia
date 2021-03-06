﻿using OpenTK;
using OpenTK.Graphics.OpenGL;
using Metanoia.Modeling;
using System.Collections.Generic;
using System.Linq;

namespace Metanoia.Rendering
{
    public enum RenderMode
    {
        Shaded,
        Textured,
        Normals,
        Colors,
        UV0,
        BoneWeight,
        Points
    }

    public class GenericRenderer
    {
        public bool HasModelSet { get => Model != null; }

        public RenderMode RenderMode { get; set; }

        private Shader GenericShader = null;
        private Buffer VertexBuffer = null;
        private Buffer IndexBuffer = null;

        private GenericSkeleton Skeleton = null;
        private GenericModel Model = null;

        private Dictionary<string, RenderTexture> Textures = new Dictionary<string, RenderTexture>();

        private Dictionary<GenericMesh, int> MeshToOffset = new Dictionary<GenericMesh, int>();

        public void SetGenericModel(GenericModel Model)
        {
            if(GenericShader == null)
            {
                GenericShader = new Shader();
                GenericShader.LoadShader("Rendering/Shaders/Generic.vert", ShaderType.VertexShader);
                GenericShader.LoadShader("Rendering/Shaders/Generic.frag", ShaderType.FragmentShader);
                GenericShader.CompileProgram();
                System.Diagnostics.Debug.WriteLine("Shader Error Log");
                System.Diagnostics.Debug.WriteLine(GenericShader.GetErrorLog());

                VertexBuffer = new Buffer(BufferTarget.ArrayBuffer);
                IndexBuffer = new Buffer(BufferTarget.ElementArrayBuffer);
            }

            ClearTextures();
            if (Model == null) return;

            Skeleton = Model.Skeleton;
            this.Model = Model;

            LoadBufferData(Model);

            List<string> neededTextures = new List<string>();
            foreach (var mesh in Model.Meshes)
                if(Model.GetDiffuseTexture(mesh) != null && !neededTextures.Contains(Model.GetMaterial(mesh).TextureDiffuse))
                    neededTextures.Add(Model.GetMaterial(mesh).TextureDiffuse);

            // load Textures
            foreach(var tex in Model.TextureBank)
            {
                RenderTexture Texture = new RenderTexture();
                Texture.LoadGenericTexture(tex.Value);
                Textures.Add(tex.Key, Texture);
                neededTextures.Remove(tex.Key);
            }

            System.Diagnostics.Debug.WriteLine($"Loaded {Textures.Count} Textures");
            System.Diagnostics.Debug.WriteLine($"Missing {string.Join(", ", neededTextures)}");
        }

        private void LoadBufferData(GenericModel Model)
        {
            MeshToOffset.Clear();
            List<GenericVertex> Vertices = new List<GenericVertex>();
            List<int> Indicies = new List<int>();
            int Offset = 0;
            foreach(GenericMesh mesh in Model.Meshes)
            {
                MeshToOffset.Add(mesh, Indicies.Count);

                Vertices.AddRange(mesh.Vertices);

                foreach(uint i in mesh.Triangles)
                    Indicies.Add((int)(i + Offset));

                Offset = Vertices.Count;
            }
            
            VertexBuffer.Bind();
            GL.BufferData(VertexBuffer.BufferTarget, Vertices.Count * GenericVertex.Stride, Vertices.ToArray(), BufferUsageHint.StaticDraw);

            IndexBuffer.Bind();
            GL.BufferData(IndexBuffer.BufferTarget, Indicies.Count * 4, Indicies.ToArray(), BufferUsageHint.StaticDraw);
        }

        public void ClearTextures()
        {
            foreach(RenderTexture texture in Textures.Values)
            {
                texture.Delete();
            }
            Textures.Clear();
        }

        public void RenderShader(Matrix4 MVP, bool renderSkeleton = false)
        {
            if (Model == null) return;

            GL.PushAttrib(AttribMask.AllAttribBits);

            GL.UseProgram(GenericShader.ProgramID);

            GL.UniformMatrix4(GenericShader.GetAttributeLocation("mvp"), false, ref MVP);
            GL.Uniform1(GenericShader.GetAttributeLocation("renderMode"), (int)RenderMode);
            //GL.Uniform3(GenericShader.GetAttributeLocation("cameraPos"), Vector3.TransformPosition(Vector3.Zero, MVP));

            var t = Skeleton.GetBindTransforms();
            if(t.Length > 0)
                GL.UniformMatrix4(GenericShader.GetAttributeLocation("bones"), t.Length, false, ref t[0].Row0.X);
            
            GL.Uniform1(GenericShader.GetAttributeLocation("selectedBone"), -1);
            if(RenderMode == RenderMode.BoneWeight && Skeleton != null)
            {
                var bi = Skeleton.Bones.FindIndex(e => e.Selected == true);
                GL.Uniform1(GenericShader.GetAttributeLocation("selectedBone"), bi);
            }

            VertexBuffer.Bind();
            IndexBuffer.Bind();

            GL.EnableVertexAttribArray(GenericShader.GetAttributeLocation("pos"));
            GL.VertexAttribPointer(GenericShader.GetAttributeLocation("pos"), 3, VertexAttribPointerType.Float, false, GenericVertex.Stride, 0);

            GL.EnableVertexAttribArray(GenericShader.GetAttributeLocation("nrm"));
            GL.VertexAttribPointer(GenericShader.GetAttributeLocation("nrm"), 3, VertexAttribPointerType.Float, false, GenericVertex.Stride, 12);

            GL.EnableVertexAttribArray(GenericShader.GetAttributeLocation("uv0"));
            GL.VertexAttribPointer(GenericShader.GetAttributeLocation("uv0"), 2, VertexAttribPointerType.Float, false, GenericVertex.Stride, 24);

            GL.EnableVertexAttribArray(GenericShader.GetAttributeLocation("clr0"));
            GL.VertexAttribPointer(GenericShader.GetAttributeLocation("clr0"), 4, VertexAttribPointerType.Float, false, GenericVertex.Stride, 32);

            GL.EnableVertexAttribArray(GenericShader.GetAttributeLocation("bone"));
            GL.VertexAttribPointer(GenericShader.GetAttributeLocation("bone"), 4, VertexAttribPointerType.Float, false, GenericVertex.Stride, 48);

            GL.EnableVertexAttribArray(GenericShader.GetAttributeLocation("weight"));
            GL.VertexAttribPointer(GenericShader.GetAttributeLocation("weight"), 4, VertexAttribPointerType.Float, false, GenericVertex.Stride, 64);

            GL.Uniform1(GenericShader.GetAttributeLocation("dif"), 1);
            
            var transparentZSorted = new List<GenericMesh>();

            transparentZSorted.AddRange(Model.Meshes);

            transparentZSorted = transparentZSorted.OrderBy(c =>
            {
                var v = Vector3.TransformPosition(c.GetBounding().Xyz, MVP);
                return -(v.Z + c.GetBounding().W);
            }).ToList();

            GL.PointSize(5f);

            foreach (var mesh in transparentZSorted)
            {
                if (mesh.Visible)
                {
                    GL.Uniform1(GenericShader.GetAttributeLocation("hasDif"), 0);

                    GL.ActiveTexture(TextureUnit.Texture1);
                    var material = Model.GetMaterial(mesh);
                    if(material != null)
                    {
                        if (material.TextureDiffuse != null && Textures.ContainsKey(material.TextureDiffuse) && Textures[material.TextureDiffuse].Loaded)
                        {
                            Textures[material.TextureDiffuse].SetFromMaterial(Model.GetMaterial(mesh));
                            GL.Uniform1(GenericShader.GetAttributeLocation("hasDif"), 1);
                        }

                        if (material.EnableBlend)
                        {
                            GL.Enable(EnableCap.Blend);
                            GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                        }
                        else
                        {
                            GL.Disable(EnableCap.Blend);
                        }
                    }
                    GL.DrawElements(RenderMode == RenderMode.Points ? PrimitiveType.Points : mesh.PrimitiveType, mesh.Triangles.Count, DrawElementsType.UnsignedInt, MeshToOffset[mesh] * 4);
                }
            }

            GL.DisableVertexAttribArray(GenericShader.GetAttributeLocation("pos"));
            GL.DisableVertexAttribArray(GenericShader.GetAttributeLocation("nrm"));
            GL.DisableVertexAttribArray(GenericShader.GetAttributeLocation("uv0"));
            GL.DisableVertexAttribArray(GenericShader.GetAttributeLocation("clr"));
            GL.DisableVertexAttribArray(GenericShader.GetAttributeLocation("bone"));
            GL.DisableVertexAttribArray(GenericShader.GetAttributeLocation("weight"));

            GL.UseProgram(0);


            if (renderSkeleton && Skeleton != null)
            {
                GL.Disable(EnableCap.DepthTest);
                
                foreach (GenericBone bone in Skeleton.Bones)
                {
                    GL.Color3(1f, 0.5f, 0.5f);
                    GL.PointSize(5f);
                    if (bone.Selected)
                    {
                        GL.Color3(0.5f, 1f, 0.5f);
                        GL.PointSize(10f);
                    }
                    GL.Begin(PrimitiveType.Points);
                    GL.Vertex3(Vector3.TransformPosition(Vector3.Zero, Skeleton.GetWorldTransform(bone, true)));
                    GL.End();
                }
                
                GL.LineWidth(1.5f);
                GL.Begin(PrimitiveType.Lines);

                foreach (GenericBone bone in Skeleton.Bones)
                {
                    if (bone.ParentIndex > -1)
                    {
                        GL.Color3(0f, 0f, 1f);
                        GL.Vertex3(Vector3.TransformPosition(Vector3.Zero, Skeleton.GetWorldTransform(bone, true)));
                        GL.Color3(0f, 1f, 0.5f);
                        GL.Vertex3(Vector3.TransformPosition(Vector3.Zero, Skeleton.GetWorldTransform(Skeleton.Bones[bone.ParentIndex], true)));
                    }
                }

                GL.End();
            }

            GL.PopAttrib();
        }

        /*public void RenderLegacy()
        {
            GL.Enable(EnableCap.Texture2D);

            GL.Enable(EnableCap.DepthTest);
            GL.Color3(1f, 1f, 1f);

            if (Model != null)
                foreach (GenericMesh mesh in Model.Meshes)
                {
                    GL.ActiveTexture(TextureUnit.Texture0);
                    if (Textures.ContainsKey(mesh.Material.TextureDiffuse))
                        Textures[mesh.Material.TextureDiffuse].SetFromMaterial(mesh.Material);

                    GL.Begin(PrimitiveType.Triangles);
                    foreach (uint t in mesh.Triangles)
                    {
                        float Col = Vector3.Dot(new Vector3(0.25f, 0.5f, 0.5f).Normalized(), mesh.Vertices[(int)t].Nrm);
                        GL.Color3(Col, Col, Col);
                        GL.TexCoord2(mesh.Vertices[(int)t].UV0);
                        GL.Vertex3(mesh.Vertices[(int)t].Pos);
                    }
                    GL.End();
                }
            
            /*if(Textures.Count > 0)
            {
                GL.MatrixMode(MatrixMode.Projection);
                GL.LoadIdentity();
                GL.MatrixMode(MatrixMode.Modelview);
                GL.LoadIdentity();
                Textures[Model.Meshes[0].Material.TextureDiffuse].Bind();

                GL.Color4(0, 1f, 0, 1f);
                GL.Begin(PrimitiveType.Quads);
                GL.TexCoord2(1, 1); GL.Vertex2(1, -1);
                GL.TexCoord2(0, 1); GL.Vertex2(-1, -1);
                GL.TexCoord2(0, 0); GL.Vertex2(-1, 1);
                GL.TexCoord2(1, 0); GL.Vertex2(1, 1);
                GL.End();
            }*/
            /*
            
            GL.PointSize(10f);
            GL.Disable(EnableCap.DepthTest);
             GL.Color3(1f, 0, 0);
            GL.Begin(PrimitiveType.Points);

            if(Skeleton != null)
            foreach (GenericBone bone in Skeleton.Bones)
            {
                GL.Vertex3(Vector3.TransformPosition(Vector3.Zero, Skeleton.GetBoneTransform(bone)));
            }

            GL.End();

            GL.LineWidth(5f);
            GL.Color3(0, 1f, 0);
            GL.Begin(PrimitiveType.Lines);

            if (Skeleton != null)
                foreach (GenericBone bone in Skeleton.Bones)
                {
                    if (bone.ParentIndex == -1) continue;
                    GL.Vertex3(Vector3.TransformPosition(Vector3.Zero, Skeleton.GetBoneTransform(bone)));
                    GL.Vertex3(Vector3.TransformPosition(Vector3.Zero, Skeleton.GetBoneTransform(Skeleton.Bones[bone.ParentIndex])));
                }

            GL.End();

        }*/
    }
}
