using NV.Client.Net;
using UnityEditor;
using UnityEngine;

namespace NV.Client.EditorTools
{
    /// 씬에 네트워크 진입점을 심는다.
    ///
    /// <see cref="NetworkBootstrap"/> 이 씬에 없으면 프로젝트는 종전대로 혼자 돌아간다.
    /// 그래서 연동을 켜는 것은 이 오브젝트를 두는 것과 같고, 끄는 것은 지우는 것과 같다.
    ///
    /// 새로 붙인 컴포넌트는 그 시점의 필드 기본값을 씬에 직렬화한다. 나중에 .cs 의
    /// 기본값을 바꿔도 씬은 갱신되지 않으므로, 값을 고칠 때는 양쪽을 함께 본다.
    public static class NetworkSetup
    {
        private const string ObjectName = "Network";

        [MenuItem("Tools/NV Network/Setup Networking")]
        public static void Setup()
        {
            var existing = Object.FindAnyObjectByType<NetworkBootstrap>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[NV] 이미 씬에 NetworkBootstrap 이 있다. 선택했다.");
                return;
            }

            var go = new GameObject(ObjectName);
            Undo.RegisterCreatedObjectUndo(go, "Setup NV Networking");
            Undo.AddComponent<NetworkBootstrap>(go);

            Selection.activeGameObject = go;
            EditorUtility.SetDirty(go);

            Debug.Log("[NV] Network 오브젝트를 만들었다. host 를 확인하고 씬을 저장한다.");
        }

        [MenuItem("Tools/NV Network/Remove Networking")]
        public static void Remove()
        {
            var existing = Object.FindAnyObjectByType<NetworkBootstrap>();
            if (existing == null)
            {
                Debug.Log("[NV] 씬에 NetworkBootstrap 이 없다.");
                return;
            }

            Undo.DestroyObjectImmediate(existing.gameObject);
            Debug.Log("[NV] 연동을 껐다. 이제 씬은 혼자 돌아간다.");
        }
    }
}
