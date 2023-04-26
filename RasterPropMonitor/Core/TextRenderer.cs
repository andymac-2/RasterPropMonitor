using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace JSI
{
    internal class TextRenderer
    {
        private class FontRenderer
        {
            public Texture2D fontTexture;
            public Mesh mesh;
            public Material fontMaterial;

            public List<Vector3> vertices = new List<Vector3>();
            public List<Vector2> uvs = new List<Vector2>();
            public List<Color32> colors32 = new List<Color32>();
            public List<ushort> indices = new List<ushort>();

            internal FontRenderer(Texture2D fontTexture, Vector2 vectorSize)
            {
                Shader displayShader = JUtil.LoadInternalShader("RPM/FontShader");

                fontMaterial = new Material(displayShader);
                fontMaterial.color = Color.white;
                fontMaterial.mainTexture = fontTexture;

                this.fontTexture = fontTexture;
                this.fontTexture.filterMode = FilterMode.Bilinear;

                mesh = new Mesh();
            }

            internal void Bake()
            {
                mesh.Clear();

                if (vertices.Count == 0)
                {
                    return;
                }

                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetColors(colors32);

                // 6 indices for each quad (4 vertices)
                int oldQuadCount = indices.Count / 6;
                int quadCount = vertices.Count / 4;
                for (int quadIndex = oldQuadCount; quadIndex < quadCount; ++quadIndex)
                {
                    ushort baseVertexIndex = (ushort)(quadIndex * 4);
                    indices.Add((ushort)(baseVertexIndex + 1));
                    indices.Add((ushort)(baseVertexIndex + 0));
                    indices.Add((ushort)(baseVertexIndex + 2));
                    indices.Add((ushort)(baseVertexIndex + 3));
                    indices.Add((ushort)(baseVertexIndex + 1));
                    indices.Add((ushort)(baseVertexIndex + 2));
                }

                mesh.SetTriangles(indices, 0, quadCount * 6, 0);
            }

            internal void Clear()
            {
                vertices.Clear();
                uvs.Clear();
                colors32.Clear();
            }

            // MOARdV TODO: Make this do something
            internal void Destroy()
            {
                UnityEngine.Object.Destroy(mesh);
                UnityEngine.Object.Destroy(fontMaterial);
            }
        }

        // The per-font (texture) renderers
        private readonly List<FontRenderer> fontRenderer;

        // pre-computed font sizes (in terms of pixel sizes)
        private readonly float fontLetterWidth;
        private readonly float fontLetterHeight;
        private readonly float fontLetterHalfHeight;
        private readonly float fontLetterHalfWidth;
        private readonly float fontLetterDoubleWidth;

        // Size of the screen in pixels
        private readonly int screenPixelWidth;
        private readonly int screenPixelHeight;

        // Offset to the top-left corner of the screen
        private readonly float screenXOffset, screenYOffset;

        // Supported characters
        private readonly Dictionary<char, Rect> fontCharacters = new Dictionary<char, Rect>();
        private readonly HashSet<char> characterWarnings = new HashSet<char>();

        // Stores the last strings we drew so we can determine if they've changed.
        private string cachedText;
        private string cachedOverlayText;

        private readonly bool manuallyInvertY;

        private enum Script
        {
            Normal,
            Subscript,
            Superscript,
        }

        private enum Width
        {
            Normal,
            Half,
            Double,
        }

        /**
         * TextRenderer (constructor)
         * 
         * Set up the TextRenderer object, and take care of the pre-computations needed.
         */
        public TextRenderer(List<Texture2D> fontTexture, Vector2 fontLetterSize, string fontDefinitionString, int drawingLayer, int screenWidth, int screenHeight)
        {
            if (fontTexture.Count == 0)
            {
                throw new Exception("No font textures found");
            }

            manuallyInvertY = false;
            //if (SystemInfo.graphicsDeviceVersion.StartsWith("Direct3D 9") || SystemInfo.graphicsDeviceVersion.StartsWith("Direct3D 11") || SystemInfo.graphicsDeviceVersion.StartsWith("Direct3D 12"))
            //{
            //    manuallyInvertY = (UnityEngine.QualitySettings.antiAliasing > 0);
            //}

            screenPixelWidth = screenWidth;
            screenPixelHeight = screenHeight;

            screenXOffset = (float)screenPixelWidth * -0.5f;
            screenYOffset = (float)screenPixelHeight * 0.5f - fontLetterSize.y;
            if (manuallyInvertY)
            {
                // This code was written for a much older flavor of Unity, and the Unity 2017.1 update broke
                // some assumptions about who managed the y-inversion issue between OpenGL and DX9.
                screenYOffset = -screenYOffset;
                screenYOffset -= fontLetterSize.y;
            }

            float fontLettersX = Mathf.Floor(fontTexture[0].width / fontLetterSize.x);
            float fontLettersY = Mathf.Floor(fontTexture[0].height / fontLetterSize.y);
            float pixelOffsetX = 0.5f / (float)fontTexture[0].width;
            float pixelOffsetY = 0.5f / (float)fontTexture[0].height;
            float letterSpanX = 1.0f / fontLettersX;
            float letterSpanY = 1.0f / fontLettersY;
            int lastCharacter = (int)fontLettersX * (int)fontLettersY;

            if (lastCharacter != fontDefinitionString.Length)
            {
                JUtil.LogMessage(this, "Warning, number of letters in the font definition does not match font bitmap size.");
            }

            // Precompute texture coordinates for all of the supported characters
            for (int i = 0; i < lastCharacter && i < fontDefinitionString.Length; i++)
            {
                int xSource = i % (int)fontLettersX;
                int ySource = (i - xSource) / (int)fontLettersX;
                if (!fontCharacters.ContainsKey(fontDefinitionString[i]))
                {
                    fontCharacters[fontDefinitionString[i]] = new Rect(letterSpanX * (float)xSource + pixelOffsetX, letterSpanY * (fontLettersY - (float)ySource - 1.0f) + pixelOffsetY, letterSpanX, letterSpanY);
                }
            }

            fontLetterWidth = fontLetterSize.x;
            fontLetterHeight = fontLetterSize.y;
            fontLetterHalfHeight = fontLetterSize.y * 0.5f;
            fontLetterHalfWidth = fontLetterSize.x * 0.5f;
            fontLetterDoubleWidth = fontLetterSize.x * 2.0f;

            fontRenderer = new List<FontRenderer>();
            for (int i = 0; i < fontTexture.Count; ++i)
            {
                FontRenderer fr = new FontRenderer(fontTexture[i], fontLetterSize);

                fontRenderer.Add(fr);
            }
        }

        static int HexDigitValue(char hexDigit)
        {
            if (hexDigit >= '0' && hexDigit <= '9') return hexDigit - '0';
            if (hexDigit >= 'A' && hexDigit <= 'F') return hexDigit - 'A' + 10;
            if (hexDigit >= 'a' && hexDigit <= 'f') return hexDigit - 'a' + 10;
            return 0;
        }

        public static Color32 ParseHexColorRGB(string text, int startIndex)
        {
            int r = HexDigitValue(text[startIndex + 0]) * 16 + HexDigitValue(text[startIndex + 1]);
            int g = HexDigitValue(text[startIndex + 2]) * 16 + HexDigitValue(text[startIndex + 3]);
            int b = HexDigitValue(text[startIndex + 4]) * 16 + HexDigitValue(text[startIndex + 5]);

            return new Color32((byte)r, (byte)g, (byte)b, 255);
        }

        public static Color32 ParseHexColorRGBA(string text, int startIndex)
        {
            int r = HexDigitValue(text[startIndex + 0]) * 16 + HexDigitValue(text[startIndex + 1]);
            int g = HexDigitValue(text[startIndex + 2]) * 16 + HexDigitValue(text[startIndex + 3]);
            int b = HexDigitValue(text[startIndex + 4]) * 16 + HexDigitValue(text[startIndex + 5]);
            int a = HexDigitValue(text[startIndex + 6]) * 16 + HexDigitValue(text[startIndex + 7]);

            return new Color32((byte)r, (byte)g, (byte)b, (byte)a);
        }

        public static int ParseInt(string text, int startIndex, int endIndex)
        {
            bool neg = false;
            if (text[startIndex] == '+')
            {
                ++startIndex;
            }
            else if (text[startIndex] == '-')
            {
                neg = true;
                ++startIndex;
            }

            int result = 0;
            while (startIndex < endIndex)
            {
                if (text[startIndex] < '0' || text[startIndex] > '9') break;
                result = result * 10 + text[startIndex] - '0';
                ++startIndex;
            }

            return neg ? -result : result;
        }

        public static bool CheckTag(string text, int startIndex, string tag)
        {
            return string.CompareOrdinal(text, startIndex, tag, 0, tag.Length) == 0;
        }

        /**
         * ParseText
         *
         * Parse the text to render, accounting for tagged values (superscript, subscript, font, nudge, etc).
         */
        private void ParseText(string textToRender, int screenXMin, int screenYMin, Color defaultColor, int pageFont)
        {
            Profiler.BeginSample("ParseText");
            if (pageFont >= fontRenderer.Count)
            {
                pageFont = 0;
            }

            float yCursor = screenYMin * fontLetterHeight;
            Color32 fontColor = defaultColor;
            float xOffset = 0.0f;
            float yOffset = 0.0f;
            Script scriptType = Script.Normal;
            Width fontWidth = Width.Normal;
            FontRenderer fr = fontRenderer[pageFont];
            bool anyWarnings = false;

            float xCursor = screenXMin * fontLetterWidth;
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
                    if (tagLength <= 1)
                    {
                        break;
                    }
                    else if (tagLength == 2)
                    {
                        if (CheckTag(textToRender, charIndex, "hw"))
                        {
                            fontWidth = Width.Half;
                            charIndex += tagLength + 1;
                        }
                        else if (CheckTag(textToRender, charIndex, "dw"))
                        {
                            fontWidth = Width.Double;
                            charIndex += tagLength + 1;
                        }
                        else
                        {
                            --charIndex; // treat this as a literal bracket
                            break;
                        }
                    }
                    else if (textToRender[charIndex] == '@')
                    {
                        // Valid nudge tags are [@x<number>] or [@y<number>] so the conditions for them is that
                        // the next symbol is @ and there are at least three, one designating the axis.
                        int nudgeAmount = ParseInt(textToRender, charIndex + 2, charIndex + tagLength);

                        switch (textToRender[charIndex+1])
                        {
                            case 'X':
                            case 'x':
                                xOffset = nudgeAmount;
                                break;
                            case 'Y':
                            case 'y':
                                yOffset = nudgeAmount;
                                break;
                        }
                        // We only consume the symbols if they did parse correctly.
                        charIndex += tagLength + 1;
                    }
                    else if (tagLength == 3)
                    {
                        if (CheckTag(textToRender, charIndex, "sup"))
                        {
                            // Superscript!
                            scriptType = Script.Superscript;
                            charIndex += tagLength + 1;
                        }
                        else if (CheckTag(textToRender, charIndex, "sub"))
                        {
                            // Subscript!
                            scriptType = Script.Subscript;
                            charIndex += tagLength + 1;
                        }
                        else if (CheckTag(textToRender, charIndex, "/hw") || CheckTag(textToRender, charIndex, "/dw"))
                        {
                            // And back...
                            fontWidth = Width.Normal;
                            charIndex += tagLength + 1;
                        }
                        else
                        {
                            --charIndex; // treat this as a literal bracket
                            break;
                        }
                    }
                    else if (tagLength == 4)
                    {
                        if (CheckTag(textToRender, charIndex, "/sup") || CheckTag(textToRender, charIndex, "/sub"))
                        {
                            // And back...
                            scriptType = Script.Normal;
                            charIndex += tagLength + 1;
                        }
                        else
                        {
                            --charIndex; // treat this as a literal bracket
                            break;
                        }
                    }
                    else if (tagLength == 7 && textToRender[charIndex] == '#')
                    {
                        fontColor = ParseHexColorRGB(textToRender, charIndex + 1);
                        charIndex += tagLength + 1;
                    }
                    else if (tagLength == 9 && textToRender[charIndex] == '#')
                    {
                        fontColor = ParseHexColorRGBA(textToRender, charIndex + 1);
                        charIndex += tagLength + 1;
                    }
                    else if (CheckTag(textToRender, charIndex, "font"))
                    {
                        tagLength -= "font".Length;
                        charIndex += "font".Length;
                        int newFontID = ParseInt(textToRender, charIndex, charIndex + tagLength);
                        
                        if (newFontID < fontRenderer.Count)
                        {
                            fr = fontRenderer[newFontID];
                        }
                        charIndex += tagLength + 1;
                    }
                    else if (textToRender[charIndex] == '[')
                    {
                        // We got a "[[]" which means an escaped opening bracket.
                        escapedBracket = true;
                        charIndex += tagLength;
                        break;
                    }
                    else
                    {
                        --charIndex; // treat this as a literal bracket
                        break;
                    }
                }

                if (charIndex >= textToRender.Length)
                {
                    break;
                }

                if (textToRender[charIndex] == '\r')
                {
                    ++charIndex;
                }

                if (textToRender[charIndex] == '\n')
                {
                    // New line: Advance yCursor, reset xCursor and the various state values.
                    yCursor += fontLetterHeight;
                    xCursor = screenXMin * fontLetterWidth;

                    fontColor = defaultColor;
                    xOffset = 0.0f;
                    yOffset = 0.0f;
                    fontWidth = Width.Normal;
                    scriptType = Script.Normal;
                    fr = fontRenderer[pageFont];
                }
                else
                {
                    float xPos = xCursor + xOffset;
                    float yPos = yCursor + yOffset;
                    if (charIndex < textToRender.Length &&
                        xPos < screenPixelWidth &&
                        xPos > -(fontWidth == Width.Normal ? fontLetterWidth : (fontWidth == Width.Half ? fontLetterHalfWidth : fontLetterDoubleWidth)) &&
                        yPos < screenPixelHeight &&
                        yPos > -fontLetterHeight)
                    {
                        char c = escapedBracket ? '[' : textToRender[charIndex];

                        if (c == ' ')
                        {
                            // skip!
                        }
                        else if (!DrawChar(fr, escapedBracket ? '[' : textToRender[charIndex], xPos, yPos, fontColor, scriptType, fontWidth))
                        {
                            anyWarnings = true;
                        }
                    }
                    switch (fontWidth)
                    {
                        case Width.Normal:
                            xCursor += fontLetterWidth;
                            break;
                        case Width.Half:
                            xCursor += fontLetterHalfWidth;
                            break;
                        case Width.Double:
                            xCursor += fontLetterDoubleWidth;
                            break;

                    }
                }
            }

            if (anyWarnings)
            {
                JUtil.LogMessage(this, "String missing characters: {0}", textToRender);
            }

            Profiler.EndSample();
        }

        /**
         * Record the vertex, uv, and color information for a single character.
         */
        private bool DrawChar(FontRenderer fr, char letter, float xPos, float yPos, Color32 letterColor, Script scriptType, Width fontWidth)
        {
            if (fontCharacters.ContainsKey(letter))
            {
                // This code was written for a much older flavor of Unity, and the Unity 2017.1 update broke
                // some assumptions about who managed the y-inversion issue between OpenGL and DX9.
                float yPosition;
                if (manuallyInvertY)
                {
                    yPosition = screenYOffset + ((scriptType == Script.Superscript) ? yPos + fontLetterHalfHeight : yPos);
                }
                else
                {
                    yPosition = screenYOffset - ((scriptType == Script.Superscript) ? yPos - fontLetterHalfHeight : yPos);
                }
                Rect pos = new Rect(screenXOffset + xPos,
                    yPosition,
                        (fontWidth == Width.Normal ? fontLetterWidth : (fontWidth == Width.Half ? fontLetterHalfWidth : fontLetterDoubleWidth)),
                        (scriptType != Script.Normal) ? fontLetterHalfHeight : fontLetterHeight);
                fr.vertices.Add(new Vector3(pos.xMin, pos.yMin, 0.0f));
                fr.vertices.Add(new Vector3(pos.xMax, pos.yMin, 0.0f));
                fr.vertices.Add(new Vector3(pos.xMin, pos.yMax, 0.0f));
                fr.vertices.Add(new Vector3(pos.xMax, pos.yMax, 0.0f));

                Rect uv = fontCharacters[letter];
                fr.uvs.Add(new Vector2(uv.xMin, (manuallyInvertY) ? uv.yMax : uv.yMin));
                fr.uvs.Add(new Vector2(uv.xMax, (manuallyInvertY) ? uv.yMax : uv.yMin));
                fr.uvs.Add(new Vector2(uv.xMin, (manuallyInvertY) ? uv.yMin : uv.yMax));
                fr.uvs.Add(new Vector2(uv.xMax, (manuallyInvertY) ? uv.yMin : uv.yMax));

                // add 1 color entry per vertex
                fr.colors32.Add(letterColor);
                fr.colors32.Add(letterColor);
                fr.colors32.Add(letterColor);
                fr.colors32.Add(letterColor);
            }
            else if (!characterWarnings.Contains(letter))
            {
                JUtil.LogMessage(this, "Warning: Attempted to print a character \"{0}\" (u{1}) not present in the font.", letter.ToString(), letter);

                characterWarnings.Add(letter);
                return false;
            }

            return true;
        }

        public bool UpdateText(MonitorPage activePage)
        {
            bool textDirty = (cachedText != activePage.ProcessedText) || (cachedOverlayText != activePage.textOverlay);

            if (textDirty)
            {
                cachedText = activePage.ProcessedText;
                cachedOverlayText = activePage.textOverlay;

                for (int i = 0; i < fontRenderer.Count; ++i)
                {
                    fontRenderer[i].Clear();
                }

                if (!string.IsNullOrEmpty(activePage.ProcessedText))
                {
                    ParseText(activePage.ProcessedText, activePage.screenXMin, activePage.screenYMin, activePage.defaultColor, activePage.pageFont);
                }

                if (!string.IsNullOrEmpty(activePage.textOverlay))
                {
                    ParseText(activePage.textOverlay, 0, 0, activePage.defaultColor, activePage.pageFont);
                }

                for (int i = 0; i < fontRenderer.Count; ++i)
                {
                    fontRenderer[i].Bake();
                }
            }

            return textDirty;
        }

        /**
         * Render the text.  Assumes screen has already been cleared, so all we have to do here
         * is prepare the text objects and draw the text.
         */
        public void Render(RenderTexture screen)
        {
            Profiler.BeginSample("TextRenderer");
            
            GL.PushMatrix();
            GL.LoadPixelMatrix(-screenPixelWidth * 0.5f, screenPixelWidth * 0.5f, -screenPixelHeight * 0.5f, screenPixelHeight * 0.5f);

            for (int i = 0; i < fontRenderer.Count; ++i)
            {
                if (fontRenderer[i].mesh.vertexCount > 0)
                {
                    fontRenderer[i].fontMaterial.SetPass(0);
                    Graphics.DrawMeshNow(fontRenderer[i].mesh, Matrix4x4.identity);
                }
            }

            GL.PopMatrix();

            Profiler.EndSample();
        }
    }
}
