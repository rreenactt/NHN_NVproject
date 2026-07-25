using UnityEngine;

/// <summary>
/// A flat mirror. A second camera is placed at the viewer's position reflected
/// across this surface and renders into a RenderTexture, which the surface then
/// samples by screen position — so the image lines up with the frame the way a
/// real reflection does, and shifts correctly as you walk past it.
///
/// Because the reflected camera is a genuine camera (not a negatively scaled
/// matrix), face culling stays correct and URP renders it in the normal loop —
/// no manual Render() call, which URP does not support anyway.
///
/// Put on a Quad whose material uses the "NV/Mirror Surface" shader.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class PlanarMirror : MonoBehaviour
{
    [Tooltip("Whose view is reflected. Defaults to Camera.main.")]
    public Camera viewerCamera;

    [Tooltip("Resolution of the reflection render texture.")]
    public int textureSize = 1024;

    [Tooltip("Layers the mirror shows. Leave the first-person arms layer OUT, or the " +
             "viewmodel arms appear floating in the reflection as well.")]
    public LayerMask reflectLayers = ~0;

    [Tooltip("Nudges the clip plane slightly behind the surface, hiding the seam where " +
             "geometry touches the mirror.")]
    public float clipPlaneOffset = 0.02f;

    [Tooltip("Flip if the reflection renders from the wrong side of the surface.")]
    public bool flipNormal = false;

    private Camera _reflectionCamera;
    private RenderTexture _reflectionTexture;
    private Renderer _surface;
    private MaterialPropertyBlock _propertyBlock;

    private static readonly int ReflectionTexId = Shader.PropertyToID("_ReflectionTex");

    /// <summary>The face the mirror reflects from.</summary>
    public Vector3 MirrorNormal => flipNormal ? -transform.forward : transform.forward;

    public Camera ReflectionCamera => _reflectionCamera;

    private void OnEnable()
    {
        _surface = GetComponent<Renderer>();
        _propertyBlock = new MaterialPropertyBlock();
        EnsureResources();
    }

    private void OnDisable()
    {
        if (_reflectionCamera != null) DestroyImmediate(_reflectionCamera.gameObject);
        if (_reflectionTexture != null) _reflectionTexture.Release();
        _reflectionCamera = null;
        _reflectionTexture = null;
    }

    private void EnsureResources()
    {
        if (viewerCamera == null) viewerCamera = Camera.main;

        if (_reflectionTexture == null)
        {
            _reflectionTexture = new RenderTexture(textureSize, textureSize, 24)
            {
                name = name + " Reflection",
                antiAliasing = 1
            };
            _reflectionTexture.Create();
        }

        if (_reflectionCamera == null)
        {
            var go = new GameObject(name + " Reflection Camera");
            go.hideFlags = HideFlags.HideAndDontSave;
            _reflectionCamera = go.AddComponent<Camera>();
            _reflectionCamera.targetTexture = _reflectionTexture;
            _reflectionCamera.enabled = true;
        }

        _surface.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetTexture(ReflectionTexId, _reflectionTexture);
        _surface.SetPropertyBlock(_propertyBlock);
    }

    private void LateUpdate()
    {
        if (viewerCamera == null) viewerCamera = Camera.main;
        if (viewerCamera == null || _reflectionCamera == null) return;

        Vector3 normal = MirrorNormal;
        Vector3 origin = transform.position;

        // Mirror the viewer through the plane. Reflecting the forward and up vectors
        // and rebuilding the rotation gives the "look back out of the mirror" pose,
        // which is what makes your own reflection face you.
        Vector3 position = ReflectPoint(viewerCamera.transform.position, origin, normal);
        Vector3 forward = ReflectVector(viewerCamera.transform.forward, normal);
        Vector3 up = ReflectVector(viewerCamera.transform.up, normal);

        _reflectionCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, up));

        _reflectionCamera.fieldOfView = viewerCamera.fieldOfView;
        _reflectionCamera.aspect = viewerCamera.aspect;
        _reflectionCamera.farClipPlane = viewerCamera.farClipPlane;
        _reflectionCamera.nearClipPlane = viewerCamera.nearClipPlane;
        _reflectionCamera.cullingMask = reflectLayers;
        _reflectionCamera.backgroundColor = viewerCamera.backgroundColor;
        _reflectionCamera.clearFlags = viewerCamera.clearFlags;

        // Clip everything in front of the mirror surface, so objects standing between
        // the mirror and its camera cannot leak into the reflection.
        Vector3 clipNormal = -normal;
        Vector3 clipPoint = origin + normal * clipPlaneOffset;
        Vector4 clipPlane = CameraSpacePlane(_reflectionCamera, clipPoint, clipNormal);
        _reflectionCamera.projectionMatrix = _reflectionCamera.CalculateObliqueMatrix(clipPlane);
    }

    private static Vector3 ReflectPoint(Vector3 point, Vector3 planeOrigin, Vector3 planeNormal)
    {
        float distance = Vector3.Dot(point - planeOrigin, planeNormal);
        return point - 2f * distance * planeNormal;
    }

    private static Vector3 ReflectVector(Vector3 direction, Vector3 planeNormal)
    {
        return direction - 2f * Vector3.Dot(direction, planeNormal) * planeNormal;
    }

    // The oblique projection wants the clip plane in the reflection camera's own space.
    private static Vector4 CameraSpacePlane(Camera camera, Vector3 point, Vector3 normal)
    {
        Matrix4x4 worldToCamera = camera.worldToCameraMatrix;
        Vector3 cameraPoint = worldToCamera.MultiplyPoint(point);
        Vector3 cameraNormal = worldToCamera.MultiplyVector(normal).normalized;
        return new Vector4(cameraNormal.x, cameraNormal.y, cameraNormal.z,
                           -Vector3.Dot(cameraPoint, cameraNormal));
    }
}
