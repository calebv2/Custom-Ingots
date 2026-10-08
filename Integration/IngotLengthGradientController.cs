using System.Collections.Generic;
using CustomIngots.API;
using UnityEngine;

namespace CustomIngots.Config.Integration;

// Keeps the gradient in mesh-local coordinates, so moving or rotating an item does not rotate
// the colors relative to its ingot, blade, or tool head.
internal sealed class IngotLengthGradientController : MonoBehaviour
{
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
    private static readonly int SpecColorId = Shader.PropertyToID("_SpecColor");
    private static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");
    private static readonly Dictionary<IngotGradient, Texture2D> ColorTextures = new Dictionary<IngotGradient, Texture2D>();
    private static Texture2D? emissionMaskTexture;
    private static Shader? gradientShader;
    private static bool shaderResolved;
    private static bool shaderWarningLogged;

    private Renderer? target;
    private MeshFilter? meshFilter;
    private SkinnedMeshRenderer? skinnedMeshRenderer;
    private Mesh? originalMesh;
    private Mesh? projectedMesh;
    private Material? originalMaterial;
    private Material? gradientMaterial;
    private IngotGradient? gradient;
    private bool warned;

    internal static bool Apply(Renderer renderer, IngotGradient? settings)
    {
        if (renderer == null) return false;
        var controller = renderer.GetComponent<IngotLengthGradientController>();
        if (settings == null)
        {
            if (controller != null) controller.Unbind();
            return false;
        }

        if (controller == null) controller = renderer.gameObject.AddComponent<IngotLengthGradientController>();
        return controller.Bind(renderer, settings);
    }

    private bool Bind(Renderer renderer, IngotGradient settings)
    {
        try
        {
            target = renderer;
            meshFilter = renderer.GetComponent<MeshFilter>();
            skinnedMeshRenderer = renderer as SkinnedMeshRenderer;
            var currentMesh = meshFilter != null ? meshFilter.sharedMesh : skinnedMeshRenderer?.sharedMesh;
            var currentMaterial = renderer.sharedMaterial;
            if (!shaderResolved)
            {
                gradientShader = Shader.Find("Standard (Specular setup)");
                shaderResolved = true;
            }
            var shader = gradientShader;
            if (shader == null && !shaderWarningLogged)
            {
                shaderWarningLogged = true;
                Core.Logger.Warning("The Standard (Specular setup) shader is unavailable; lengthwise gradients will use the configured tint.");
            }
            if (currentMesh == null || currentMaterial == null || shader == null) return false;

            if (currentMesh != projectedMesh || gradient?.Reverse != settings.Reverse)
            {
                var baseMesh = currentMesh == projectedMesh ? originalMesh : currentMesh;
                if (baseMesh == null) return false;
                var replacement = ProjectAlongLongestAxis(baseMesh, settings.Reverse);
                if (replacement == null) return false;
                if (projectedMesh != null) Destroy(projectedMesh);
                originalMesh = baseMesh;
                projectedMesh = replacement;
                if (meshFilter != null) meshFilter.sharedMesh = replacement;
                else if (skinnedMeshRenderer != null) skinnedMeshRenderer.sharedMesh = replacement;
            }

            if (currentMaterial != gradientMaterial || !ReferenceEquals(gradient, settings))
            {
                if (currentMaterial != gradientMaterial) originalMaterial = currentMaterial;
                if (originalMaterial == null) return false;
                var replacement = new Material(originalMaterial)
                {
                    name = originalMaterial.name + " Long Axis Gradient"
                };
                replacement.shader = shader;
                replacement.SetTexture(MainTexId, GetColorTexture(settings));
                replacement.SetTexture(EmissionMapId, GetEmissionMaskTexture());
                replacement.SetColor(ColorId, Color.white);
                replacement.SetColor(SpecColorId, new Color(0.08f, 0.08f, 0.08f, 1f));
                if (originalMaterial.HasProperty(GlossinessId))
                    replacement.SetFloat(GlossinessId, originalMaterial.GetFloat(GlossinessId));
                replacement.EnableKeyword("_EMISSION");
                if (gradientMaterial != null) Destroy(gradientMaterial);
                gradientMaterial = replacement;
                renderer.sharedMaterial = replacement;
                gradient = settings;
            }
            return projectedMesh != null && gradientMaterial != null;
        }
        catch (System.Exception exception)
        {
            Unbind();
            if (!warned)
            {
                warned = true;
                Core.Logger.Warning("Could not apply long-axis gradient to " + renderer.name + ": " + exception.Message);
            }
            return false;
        }
    }

    private static Mesh? ProjectAlongLongestAxis(Mesh source, bool reverse)
    {
        var bounds = source.bounds;
        var size = bounds.size;
        var axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        var extent = axis == 0 ? size.x : axis == 1 ? size.y : size.z;
        if (extent <= 0.000001f) return null;

        var vertices = source.vertices;
        if (vertices.Length == 0) return null;
        var start = axis == 0 ? bounds.min.x : axis == 1 ? bounds.min.y : bounds.min.z;
        var uv = new Vector2[vertices.Length];
        for (var index = 0; index < vertices.Length; index++)
        {
            var position = axis == 0 ? vertices[index].x : axis == 1 ? vertices[index].y : vertices[index].z;
            var progress = Mathf.Clamp01((position - start) / extent);
            uv[index] = new Vector2(reverse ? 1f - progress : progress, 0.5f);
        }

        var clone = Instantiate(source);
        clone.name = source.name + " Long Axis Gradient";
        clone.uv = uv;
        return clone;
    }

    private static Texture2D GetColorTexture(IngotGradient settings)
    {
        if (ColorTextures.TryGetValue(settings, out var existing)) return existing;
        const int width = 256;
        var texture = new Texture2D(width, 2, TextureFormat.RGBA32, false)
        {
            name = "Custom Ingots Lengthwise Color",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var pixels = new Color32[width * 2];
        for (var x = 0; x < width; x++)
        {
            var pixel = (Color32)Color.Lerp(settings.Start, settings.End, x / (width - 1f));
            pixels[x] = pixel;
            pixels[width + x] = pixel;
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        ColorTextures.Add(settings, texture);
        return texture;
    }

    private static Texture2D GetEmissionMaskTexture()
    {
        if (emissionMaskTexture != null) return emissionMaskTexture;
        const int width = 256;
        var texture = new Texture2D(width, 2, TextureFormat.RGBA32, false)
        {
            name = "Custom Ingots Lengthwise Glow Mask",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var pixels = new Color32[width * 2];
        for (var x = 0; x < width; x++)
        {
            var pixel = new Color32((byte)x, (byte)x, (byte)x, 255);
            pixels[x] = pixel;
            pixels[width + x] = pixel;
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        emissionMaskTexture = texture;
        return texture;
    }

    private void Unbind()
    {
        if (target != null)
        {
            if (meshFilter != null && meshFilter.sharedMesh == projectedMesh) meshFilter.sharedMesh = originalMesh;
            else if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh == projectedMesh)
                skinnedMeshRenderer.sharedMesh = originalMesh;
            if (target.sharedMaterial == gradientMaterial) target.sharedMaterial = originalMaterial;
        }
        ReleaseOwnedObjects();
        originalMesh = null;
        originalMaterial = null;
        gradient = null;
    }

    private void OnDestroy() => ReleaseOwnedObjects();

    private void ReleaseOwnedObjects()
    {
        if (projectedMesh != null) Destroy(projectedMesh);
        if (gradientMaterial != null) Destroy(gradientMaterial);
        projectedMesh = null;
        gradientMaterial = null;
    }
}
