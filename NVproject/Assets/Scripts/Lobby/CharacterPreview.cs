using UnityEngine;

namespace NV.Client.Lobby
{
    /// 캐릭터 하나를 세워 놓고 렌더 텍스처로 뽑는 작은 무대.
    ///
    /// UI Toolkit 은 3D 를 그리지 못하므로 카메라 하나가 그린 것을 배경 이미지로 넘긴다.
    /// `LobbyMannequin` 을 그대로 쓴다 — 블록 비율과 절차적 idle 이 이미 들어 있고, 옛
    /// 로비에서 남길 값이 있는 파일이 이것 하나였다.
    ///
    /// **레이어를 새로 만들지 않는다.** 전용 컬링 레이어가 정석이지만 그것은
    /// `ProjectSettings/TagManager.asset` 을 고치는 일이고, 무대를 멀리 세우면 같은 결과를
    /// 얻는다 — 메인 로비 씬에는 UI 말고 아무것도 없으므로 카메라의 좁은 시야에 이 인형만
    /// 들어온다. 나중에 이 씬에 무엇이 생기면 그때 레이어가 필요해진다.
    ///
    /// 카메라는 `targetTexture` 가 있으면 매 프레임 스스로 그린다. 그래서 idle 이 움직이고,
    /// 이 클래스에는 갱신 루프가 없다.
    public sealed class CharacterPreview
    {
        /// 무대를 세우는 곳. 원점에서 충분히 멀어 씬의 다른 것이 프레임에 들어오지 않는다.
        private static readonly Vector3 StageOrigin = new Vector3(0f, 0f, 2000f);

        private readonly GameObject _root;
        private readonly LobbyMannequin _mannequin;
        private readonly Camera _camera;
        private readonly RenderTexture _texture;

        private byte _shown = 0xFF;

        private CharacterPreview(GameObject root, LobbyMannequin mannequin, Camera camera, RenderTexture texture)
        {
            _root = root;
            _mannequin = mannequin;
            _camera = camera;
            _texture = texture;
        }

        /// 화면에 넘기는 그림. 파괴된 뒤에는 null 이다.
        public RenderTexture Texture => _texture;

        /// 무대를 만든다. 실패하면 null — 셰이더가 없는 빌드에서는 인형을 세울 수 없다.
        public static CharacterPreview Create(int width = 256, int height = 384)
        {
            var root = new GameObject("Lobby Character Preview");
            root.transform.position = StageOrigin;

            var mannequin = LobbyMannequin.Spawn(root.transform, 0);

            if (mannequin == null)
            {
                Object.Destroy(root);
                return null;
            }

            // 인형은 무대 원점에 선다. 카메라는 살짝 위에서 내려다보며 몸통을 겨눈다 —
            // 정면 정중앙은 증명사진처럼 보이고, 이 게임의 로비는 그런 화면이 아니다.
            mannequin.transform.localPosition = Vector3.zero;
            mannequin.transform.localRotation = Quaternion.Euler(0f, 18f, 0f);

            var camera = new GameObject("Preview Camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform, false);
            camera.transform.localPosition = new Vector3(0f, 1.05f, -2.4f);
            camera.transform.localRotation = Quaternion.Euler(2f, 0f, 0f);
            camera.fieldOfView = 34f;
            camera.nearClipPlane = 0.05f;

            // 원경을 잘라 낸다. 이 무대 밖의 것이 배경에 들어오지 않게 하는 두 번째 장치다.
            camera.farClipPlane = 12f;
            camera.clearFlags = CameraClearFlags.SolidColor;

            // 배경은 로비 패널과 같은 갈색-검정이다. 투명으로 두면 UI 쪽 합성에 기대야 한다.
            camera.backgroundColor = new Color(0.055f, 0.047f, 0.031f, 1f);

            // **오디오 리스너를 붙이지 않는다.** 새 카메라를 만들면 붙이고 싶어지지만, 씬에
            // 리스너가 둘이면 유니티가 매 프레임 경고를 낸다.
            var texture = new RenderTexture(width, height, 16, RenderTextureFormat.Default)
            {
                name = "Lobby Character Preview",
            };

            camera.targetTexture = texture;

            // 실내 조명 하나. 없으면 URP 에서 인형이 새까맣게 나온다.
            var light = new GameObject("Preview Light").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localRotation = Quaternion.Euler(28f, 205f, 0f);
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.97f, 0.86f);
            light.intensity = 1.35f;
            light.shadows = LightShadows.None;

            return new CharacterPreview(root, mannequin, camera, texture);
        }

        /// 이 캐릭터를 입힌다. 같은 번호를 다시 주면 아무 일도 하지 않는다.
        ///
        /// 다시 입히는 일을 막는 이유는 `ApplyCharacter` 가 머리 장식을 다시 만들기 때문이다.
        /// 2Hz 로 그것을 부르면 프레임마다 GameObject 가 생기고 사라진다.
        public void Show(byte characterId)
        {
            if (_shown == characterId)
            {
                return;
            }

            LobbyCharacterCatalog.Character character = LobbyCharacterCatalog.At(characterId);

            if (character == null)
            {
                return;
            }

            _shown = characterId;
            _mannequin.ApplyCharacter(character);
        }

        public void Dispose()
        {
            if (_camera != null)
            {
                // 텍스처를 먼저 떼지 않고 지우면 아직 그것을 그리려는 카메라가 남는다.
                _camera.targetTexture = null;
            }

            if (_texture != null)
            {
                _texture.Release();
                Object.Destroy(_texture);
            }

            if (_root != null)
            {
                Object.Destroy(_root);
            }
        }
    }
}
