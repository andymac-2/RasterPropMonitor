/*****************************************************************************
 * RasterPropMonitor
 * =================
 * Plugin for Kerbal Space Program
 *
 *  by Mihara (Eugene Medvedev), MOARdV, and other contributors
 * 
 * RasterPropMonitor is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, revision
 * date 29 June 2007, or (at your option) any later version.
 * 
 * RasterPropMonitor is distributed in the hope that it will be useful, but
 * WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
 * or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License
 * for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with RasterPropMonitor.  If not, see <http://www.gnu.org/licenses/>.
 ****************************************************************************/
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace JSI
{
    /// <summary>
    /// JSITextMesh is designed as a drop-in replacement for Unity's TextMesh
    /// with two key differences:
    /// 1) the Material and Mesh are both directly visible from the class, and
    /// 2) the generated mesh includes normals and tangents, making this class
    /// suitable for in-scene lighting.
    /// </summary>
    public class JSITextMesh : MonoBehaviour
    {
        private TextAlignment alignment_;
        public TextAlignment alignment
        {
            get
            {
                return alignment_;
            }
            set
            {
                if (value != alignment_)
                {
                    invalidated = true;
                    alignment_ = value;
                    enabled = true;
                }
            }
        }

        private TextAnchor anchor_;
        public TextAnchor anchor
        {
            get
            {
                return anchor_;
            }
            set
            {
                if (value != anchor_)
                {
                    invalidated = true;
                    anchor_ = value;
                    enabled = true;
                }
            }
        }

        private float characterSize_ = 1.0f;
        public float characterSize
        {
            get
            {
                return characterSize_;
            }
            set
            {
                if (value != characterSize_)
                {
                    invalidated = true;
                    characterSize_ = value;
                    enabled = true;
                }
            }
        }

        private Color32 color_;
        public Color32 color
        {
            get
            {
                return color_;
            }
            set
            {
                if (value.r != color_.r || value.g != color_.g || value.b != color_.b || value.a != color_.a)
                {
                    invalidatedColor = true;
                    color_ = value;
                    enabled = true;
                }
            }
        }

        private Font font_;
        public Font font
        {
            get
            {
                return font_;
            }
            set
            {
                if (value != font_)
                {
                    invalidated = true;
                    font_ = value;
                    enabled = true;
                    if (font_ != null)
                    {
                        CreateComponents();
                        meshRenderer_.material.mainTexture = font_.material.mainTexture;
                    }
                }
            }
        }

        private int fontSize_ = 32;
        public int fontSize
        {
            get
            {
                return fontSize_;
            }
            set
            {
                if (value != fontSize_)
                {
                    invalidated = true;
                    enabled = true;
                    fontSize_ = value;
                }
            }
        }

        private FontStyle fontStyle_;
        public FontStyle fontStyle
        {
            get
            {
                return fontStyle_;
            }
            set
            {
                if (value != fontStyle_)
                {
                    invalidated = true;
                    enabled = true;
                    fontStyle_ = value;
                }
            }
        }

        private float lineSpacing_ = 1.0f;
        public float lineSpacing
        {
            get
            {
                return lineSpacing_;
            }
            set
            {
                if (value != lineSpacing_)
                {
                    invalidated = true;
                    enabled = true;
                    lineSpacing_ = value;
                }
            }
        }

        private MeshRenderer meshRenderer_;
        private MeshFilter meshFilter_;
        public Material material
        {
            get
            {
                CreateComponents();
                return meshRenderer_.material;
            }
        }

        public Mesh mesh
        {
            get
            {
                CreateComponents();
                return meshFilter_.mesh;
            }
        }

        private string text_;
        private bool richText = false;
        public string text
        {
            get
            {
                return text_;
            }
            set
            {
                if (value != text_)
                {
                    invalidated = true;
                    enabled = true;
                    text_ = value;

                    if (meshRenderer_ != null)
                    {
                        if (string.IsNullOrEmpty(text_))
                        {
                            meshRenderer_.gameObject.SetActive(false);
                        }
                        else
                        {
                            meshRenderer_.gameObject.SetActive(true);
                        }
                    }
                }
            }
        }

        private bool invalidated = false;
        private bool invalidatedColor = false;
        private bool fontNag = false;

        List<Vector3> vertices = new List<Vector3>();
        List<Color32> colors32 = new List<Color32>();
        List<Vector4> tangents = new List<Vector4>();
        List<Vector2> uv = new List<Vector2>();
        List<int> triangles = new List<int>();

        /// <summary>
        /// Set up rendering components.
        /// </summary>
        private void CreateComponents()
        {
            if (meshRenderer_ == null)
            {
                meshFilter_ = gameObject.AddComponent<MeshFilter>();
                meshRenderer_ = gameObject.AddComponent<MeshRenderer>();
                meshRenderer_.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                meshRenderer_.receiveShadows = true; // not working?
                meshRenderer_.material = new Material(JUtil.LoadInternalShader("RPM/JSILabel"));

                var enabler = gameObject.AddComponent<VisibilityEnabler>();
                enabler.Initialize(this);
            }
        }

        /// <summary>
        /// Set up the JSITextMesh components if they haven't been set up yet.
        /// </summary>
        public void Start()
        {
            Font.textureRebuilt += FontRebuiltCallback;
            CreateComponents();
        }

        /// <summary>
        /// Make sure we don't leave our callback lingering.
        /// </summary>
        public void OnDestroy()
        {
            Font.textureRebuilt -= FontRebuiltCallback;

            Destroy(meshFilter_);
            meshFilter_ = null;

            Destroy(meshRenderer_.material);

            Destroy(meshRenderer_);
            meshRenderer_ = null;
        }

        /// <summary>
        /// Callback to tell us when a Font had to rebuild its texture atlas.
        /// When that happens, we have to regenerate our text.
        /// </summary>
        /// <param name="whichFont"></param>
        private void FontRebuiltCallback(Font whichFont)
        {
            if (whichFont == font_)
            {
                invalidated = true;
                meshRenderer_.material.mainTexture = font_.material.mainTexture;
                enabled = true;
            }
        }

        /// <summary>
        /// Update the text mesh if it's changed.
        /// </summary>
        public void Update()
        {
            if (!string.IsNullOrEmpty(text_))
            {
                if (invalidated)
                {
                    if (font_ == null)
                    {
                        if (!fontNag)
                        {
                            JUtil.LogErrorMessage(this, "Font was not initialized");
                            JUtil.AnnoyUser(this);
                            fontNag = true;
                        }
                        return;
                    }

                    if (text_.IndexOf('[') != -1)
                    {
                        richText = true;
                        GenerateRichText();
                    }
                    else
                    {
                        richText = false;
                        GenerateText();
                    }

                    invalidated = false;
                    invalidatedColor = false;
                }
                else if (invalidatedColor)
                {
                    if (richText)
                    {
                        GenerateRichText();
                    }
                    else
                    {
                        if (meshFilter_.mesh.colors32.Length > 0)
                        {
                            Color32[] newColor = new Color32[meshFilter_.mesh.colors32.Length];
                            for (int idx = 0; idx < newColor.Length; ++idx)
                            {
                                newColor[idx] = color_;
                            }
                            meshFilter_.mesh.colors32 = newColor;
                            meshFilter_.mesh.UploadMeshData(false);
                        }
                    }

                    invalidatedColor = false;
                }
            }

            enabled = false;
        }

        /// <summary>
        /// Convert a text using control sequences ([b], [i], [#rrggbb(aa)], [size]).
        /// </summary>
        private void GenerateRichText()
        {
            // Break the text into lines
            string[] textLines = text_.Split(JUtil.LineSeparator, StringSplitOptions.None);

            // State tracking
            bool bold = false;
            bool italic = false;
            //size = something.

            // Determine text length
            int[] textLength = new int[textLines.Length];
            int maxTextLength = 0;
            int maxVerts = 0;

            // TODO: this loop is just to *measure* the text - it might be better to parse and measure it in one loop?
            for (int line = 0; line < textLines.Length; ++line)
            {
                textLength[line] = 0;
                string textToRender = textLines[line];

                for (int charIndex = 0; charIndex < textToRender.Length; charIndex++)
                {
                    bool escapedBracket = false;
                    // We will continue parsing bracket pairs until we're out of bracket pairs,
                    // since all of them -- except the escaped bracket tag --
                    // consume characters and change state without actually generating any output.
                    while (charIndex < textToRender.Length && textToRender[charIndex] == '[')
                    {
                        ++charIndex;
                        if (charIndex >= textToRender.Length) break;

                        // If there's no closing bracket, we stop parsing and go on to printing.
                        int tagLength = textToRender.IndexOf(']', charIndex) - charIndex;
                        if (tagLength <= 0)
                        {
                            break;
                        }
                        else if (tagLength == 1)
                        {
                            if (textToRender[charIndex] == 'b')
                            {
                                bold = true;
                                charIndex += tagLength + 1;
                            }
                            else if (textToRender[charIndex] == 'i')
                            {
                                italic = true;
                                charIndex += tagLength + 1;
                            }
                            else if (textToRender[charIndex] == '[')
                            {
                                // We got a "[[]" which means an escaped opening bracket.
                                escapedBracket = true;
                                charIndex += tagLength;
                                break;
                            }
                        }
                        else if (tagLength == 2)
                        {
                            if (TextRenderer.CheckTag(textToRender, charIndex, "/i"))
                            {
                                italic = false;
                                charIndex += tagLength + 1;
                            }
                            else if (TextRenderer.CheckTag(textToRender, charIndex, "/b"))
                            {
                                bold = false;
                                charIndex += tagLength + 1;
                            }
                        }
                        else if (tagLength == 7 && textToRender[charIndex] == '#')
                        {
                            charIndex += tagLength + 1;
                        }
                        else if (tagLength == 9 && textToRender[charIndex] == '#')
                        {
                            charIndex += tagLength + 1;
                        }
                        else // Else we didn't recognise anything so it's not a tag.
                        {
                            break;
                        }
                    }

                    if (charIndex < textLines[line].Length)
                    {
                        FontStyle style = GetFontStyle(bold, italic);
                        font_.RequestCharactersInTexture(escapedBracket ? "[" : textToRender[charIndex].ToString(), fontSize_, style);
                        CharacterInfo charInfo;
                        if (font_.GetCharacterInfo(textToRender[charIndex], out charInfo, fontSize_, style))
                        {
                            textLength[line] += charInfo.advance;
                            maxVerts += 4;
                        }
                    }
                }

                if (textLength[line] > maxTextLength)
                {
                    maxTextLength = textLength[line];
                }
            }

            if (maxVerts == 0)
            {
                meshRenderer_.gameObject.SetActive(false);
                return;
            }

            meshRenderer_.gameObject.SetActive(true);

            PrepBuffers(maxVerts);

            int yPos = 0;
            int xAnchor = 0;
            switch (anchor_)
            {
                case TextAnchor.LowerCenter:
                    yPos = (int)(lineSpacing_ * font_.lineHeight * textLines.Length) - font_.ascent;
                    break;
                case TextAnchor.LowerLeft:
                    //xAnchor = 0;
                    yPos = (int)(lineSpacing_ * font_.lineHeight * textLines.Length) - font_.ascent;
                    break;
                case TextAnchor.LowerRight:
                    yPos = (int)(lineSpacing_ * font_.lineHeight * textLines.Length) - font_.ascent;
                    break;
                case TextAnchor.MiddleCenter:
                    yPos = (int)(lineSpacing_ * font_.lineHeight * textLines.Length) / 2 - font_.ascent;
                    break;
                case TextAnchor.MiddleLeft:
                    //xAnchor = 0;
                    yPos = (int)(lineSpacing_ * font_.lineHeight * textLines.Length) / 2 - font_.ascent;
                    break;
                case TextAnchor.MiddleRight:
                    yPos = (int)(lineSpacing_ * font_.lineHeight * textLines.Length) / 2 - font_.ascent;
                    break;
                case TextAnchor.UpperCenter:
                    yPos = -font_.ascent;
                    break;
                case TextAnchor.UpperLeft:
                    //xAnchor = 0;
                    yPos = -font_.ascent;
                    break;
                case TextAnchor.UpperRight:
                    yPos = -font_.ascent;
                    break;
            }

            int lineAdvance = (int)(lineSpacing_ * font_.lineHeight);
            for (int line = 0; line < textLines.Length; ++line)
            {
                int xPos = 0;
                if (alignment_ == TextAlignment.Center)
                {
                    xPos = -(textLength[line]) / 2;
                }
                else if (alignment_ == TextAlignment.Right)
                {
                    xPos = -textLength[line];
                }
                xPos += xAnchor;

                Color32 fontColor = color_;
                string textToRender = textLines[line];

                for (int charIndex = 0; charIndex < textToRender.Length; charIndex++)
                {
                    bool escapedBracket = false;
                    // We will continue parsing bracket pairs until we're out of bracket pairs,
                    // since all of them -- except the escaped bracket tag --
                    // consume characters and change state without actually generating any output.
                    while (charIndex < textToRender.Length && textToRender[charIndex] == '[')
                    {
                        ++charIndex;
                        if (charIndex >= textToRender.Length) break;

                        // If there's no closing bracket, we stop parsing and go on to printing.
                        int tagLength = textToRender.IndexOf(']', charIndex) - charIndex;
                        if (tagLength <= 0)
                        {
                            break;
                        }
                        else if (tagLength == 1)
                        {
                            if (textToRender[charIndex] == 'b')
                            {
                                bold = true;
                                charIndex += tagLength + 1;
                            }
                            else if (textToRender[charIndex] == 'i')
                            {
                                italic = true;
                                charIndex += tagLength + 1;
                            }
                            else if (textToRender[charIndex] == '[')
                            {
                                // We got a "[[]" which means an escaped opening bracket.
                                escapedBracket = true;
                                charIndex += tagLength;
                                break;
                            }
                        }
                        else if (tagLength == 2)
                        {
                            if (TextRenderer.CheckTag(textToRender, charIndex, "/i"))
                            {
                                italic = false;
                                charIndex += tagLength + 1;
                            }
                            else if (TextRenderer.CheckTag(textToRender, charIndex, "/b"))
                            {
                                bold = false;
                                charIndex += tagLength + 1;
                            }
                        }
                        else if (tagLength == 7 && textToRender[charIndex] == '#')
                        {
                            fontColor = TextRenderer.ParseHexColorRGB(textToRender, charIndex + 1);
                            charIndex += tagLength + 1;
                        }
                        else if (tagLength == 9 && textToRender[charIndex] == '#')
                        {
                            fontColor = TextRenderer.ParseHexColorRGBA(textToRender, charIndex + 1);
                            charIndex += tagLength + 1;
                        }
                        else // Else we didn't recognise anything so it's not a tag.
                        {
                            break;
                        }
                    }

                    if (charIndex < textLines[line].Length)
                    {
                        FontStyle style = GetFontStyle(bold, italic);
                        CharacterInfo charInfo;
                        if (font_.GetCharacterInfo(escapedBracket ? '[' : textLines[line][charIndex], out charInfo, 0, style))
                        {
                            if (charInfo.minX != charInfo.maxX && charInfo.minY != charInfo.maxY)
                            {
                                vertices.Add(new Vector3(characterSize_ * (float)(xPos + charInfo.minX), characterSize_ * (float)(yPos + charInfo.maxY), 0.0f));
                                colors32.Add(fontColor);
                                uv.Add(charInfo.uvTopLeft);

                                vertices.Add(new Vector3(characterSize_ * (float)(xPos + charInfo.maxX), characterSize_ * (float)(yPos + charInfo.maxY), 0.0f));
                                colors32.Add(fontColor);
                                uv.Add(charInfo.uvTopRight);

                                vertices.Add(new Vector3(characterSize_ * (float)(xPos + charInfo.minX), characterSize_ * (float)(yPos + charInfo.minY), 0.0f));
                                colors32.Add(fontColor);
                                uv.Add(charInfo.uvBottomLeft);

                                vertices.Add(new Vector3(characterSize_ * (float)(xPos + charInfo.maxX), characterSize_ * (float)(yPos + charInfo.minY), 0.0f));
                                colors32.Add(fontColor);
                                uv.Add(charInfo.uvBottomRight);
                            }
                            xPos += charInfo.advance;
                        }
                    }
                }

                yPos -= lineAdvance;
            }

            PopulateMesh();
        }

        void PopulateMesh()
        {
            meshFilter_.mesh.Clear();
            meshFilter_.mesh.SetVertices(vertices, 0, vertices.Count);
            meshFilter_.mesh.SetColors(colors32, 0, colors32.Count);
            meshFilter_.mesh.SetTangents(tangents, 0, vertices.Count); // note, tangents list might be longer than the vertex array
            meshFilter_.mesh.SetUVs(0, uv, 0, uv.Count);
            meshFilter_.mesh.SetTriangles(triangles, 0, vertices.Count / 4 * 6, 0);
            meshFilter_.mesh.RecalculateNormals();
            // Can't hide mesh with (true), or we can't edit colors later.
            meshFilter_.mesh.UploadMeshData(false);
        }

        private void PrepBuffers(int maxVerts)
        {
            vertices.Capacity = Math.Max(vertices.Capacity, maxVerts);
            colors32.Capacity = Math.Max(colors32.Capacity, maxVerts);
            tangents.Capacity = Math.Max(tangents.Capacity, maxVerts);
            uv.Capacity = Math.Max(uv.Capacity, maxVerts);

            vertices.Clear();
            colors32.Clear();
            uv.Clear();

            // these never change, so we populate it once and leave it
            for (int tangentCount = tangents.Count; tangentCount < maxVerts; ++tangentCount)
            {
                tangents.Add(new Vector4(1.0f, 0.0f, 0.0f, 1.0f));
            }

            // "triangles" is really "indices" and there are 6 per character
            int oldNumQuads = triangles.Count / 6;
            int newNumQuads = maxVerts / 4;
            triangles.Capacity = Math.Max(triangles.Capacity, newNumQuads / 4 * 6);
            for (int quadIndex = oldNumQuads; quadIndex < newNumQuads; ++quadIndex)
            {
                int baseVertexIndex = quadIndex * 4;
                triangles.Add(baseVertexIndex + 0);
                triangles.Add(baseVertexIndex + 3);
                triangles.Add(baseVertexIndex + 2);
                triangles.Add(baseVertexIndex + 0);
                triangles.Add(baseVertexIndex + 1);
                triangles.Add(baseVertexIndex + 3);
            }
        }

        /// <summary>
        /// Convert a simple text string into displayable quads with no
        /// additional processing (untagged text).
        /// </summary>
        private void GenerateText()
        {
            // Break the text into lines
            string[] textLines = text_.Split(JUtil.LineSeparator, StringSplitOptions.None);

            // Determine text length
            int[] textLength = new int[textLines.Length];
            int maxTextLength = 0;
            int maxVerts = 0;
            for (int line = 0; line < textLines.Length; ++line)
            {
                textLength[line] = 0;
                font_.RequestCharactersInTexture(textLines[line], fontSize_);
                maxVerts += Font.GetMaxVertsForString(textLines[line]);

                for (int ch = 0; ch < textLines[line].Length; ++ch)
                {
                    CharacterInfo charInfo;
                    if (font_.GetCharacterInfo(textLines[line][ch], out charInfo))
                    {
                        textLength[line] += charInfo.advance;
                    }
                }
                if (textLength[line] > maxTextLength)
                {
                    maxTextLength = textLength[line];
                }
            }

            if (maxVerts == 0)
            {
                meshRenderer_.gameObject.SetActive(false);
                return;
            }

            meshRenderer_.gameObject.SetActive(true);

            PrepBuffers(maxVerts);

            int yPos = 0;
            int xAnchor = 0;
            switch (anchor_)
            {
                case TextAnchor.LowerCenter:
                    yPos = (int)(lineSpacing_ * font_.lineHeight * textLines.Length) - font_.ascent;
                    break;
                case TextAnchor.LowerLeft:
                    //xAnchor = 0;
                    yPos = (int)(lineSpacing_ * font_.lineHeight * textLines.Length) - font_.ascent;
                    break;
                case TextAnchor.LowerRight:
                    yPos = (int)(lineSpacing_ * font_.lineHeight * textLines.Length) - font_.ascent;
                    break;
                case TextAnchor.MiddleCenter:
                    yPos = (int)(lineSpacing_ * font_.lineHeight * textLines.Length) / 2 - font_.ascent;
                    break;
                case TextAnchor.MiddleLeft:
                    //xAnchor = 0;
                    yPos = (int)(lineSpacing_ * font_.lineHeight * textLines.Length) / 2 - font_.ascent;
                    break;
                case TextAnchor.MiddleRight:
                    yPos = (int)(lineSpacing_ * font_.lineHeight * textLines.Length) / 2 - font_.ascent;
                    break;
                case TextAnchor.UpperCenter:
                    yPos = -font_.ascent;
                    break;
                case TextAnchor.UpperLeft:
                    //xAnchor = 0;
                    yPos = -font_.ascent;
                    break;
                case TextAnchor.UpperRight:
                    yPos = -font_.ascent;
                    break;
            }

            int lineAdvance = (int)(lineSpacing_ * font_.lineHeight);
            for (int line = 0; line < textLines.Length; ++line)
            {
                int xPos = 0;
                if (alignment_ == TextAlignment.Center)
                {
                    xPos = -(textLength[line]) / 2;
                }
                else if (alignment_ == TextAlignment.Right)
                {
                    xPos = -textLength[line];
                }
                xPos += xAnchor;

                for (int ch = 0; ch < textLines[line].Length; ++ch)
                {
                    CharacterInfo charInfo;
                    if (font_.GetCharacterInfo(textLines[line][ch], out charInfo))
                    {
                        vertices.Add(new Vector3(characterSize_ * (float)(xPos + charInfo.minX), characterSize_ * (float)(yPos + charInfo.maxY), 0.0f));
                        colors32.Add(color_);
                        uv.Add(charInfo.uvTopLeft);

                        vertices.Add(new Vector3(characterSize_ * (float)(xPos + charInfo.maxX), characterSize_ * (float)(yPos + charInfo.maxY), 0.0f));
                        colors32.Add(color_);
                        uv.Add(charInfo.uvTopRight);

                        vertices.Add(new Vector3(characterSize_ * (float)(xPos + charInfo.minX), characterSize_ * (float)(yPos + charInfo.minY), 0.0f));
                        colors32.Add(color_);
                        uv.Add(charInfo.uvBottomLeft);

                        vertices.Add(new Vector3(characterSize_ * (float)(xPos + charInfo.maxX), characterSize_ * (float)(yPos + charInfo.minY), 0.0f));
                        colors32.Add(color_);
                        uv.Add(charInfo.uvBottomRight);

                        xPos += charInfo.advance;
                    }
                }

                yPos -= lineAdvance;
            }

            PopulateMesh();
        }

        /// <summary>
        /// Convert the booleans for bold and italic text into a FontStyle.
        /// </summary>
        /// <param name="bold">Is the style bold?</param>
        /// <param name="italic">Is the style italic?</param>
        /// <returns></returns>
        public static FontStyle GetFontStyle(bool bold, bool italic)
        {
            if (bold)
            {
                return (italic) ? FontStyle.BoldAndItalic : FontStyle.Bold;
            }
            else if (italic)
            {
                return FontStyle.Italic;
            }
            else
            {
                return FontStyle.Normal;
            }
        }
    }
}
