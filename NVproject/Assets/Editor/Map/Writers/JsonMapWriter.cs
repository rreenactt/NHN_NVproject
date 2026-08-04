using System.Text;
using NV.Shared.Collision;

namespace NV.Client.EditorTools.Writers
{
    /// 서버가 읽는 JSON. **직렬화를 다시 구현하지 않고 `MapExportPipeline.Serialize` 를 부른다.**
    ///
    /// 옮겨 오고 싶어지는 코드지만 옮기면 안 된다. 그 함수는 손으로 쓴 JSON 이고, 부동소수점을
    /// 왕복 보존 형식으로 쓰는 것과 격자를 base64 로 쓰는 것과 출처를 정확히 한 줄로 쓰는 것이
    /// 전부 다른 곳의 전제다 — 특히 "출처 줄만 빼고 비교한다" 는 `ComparisonKey` 가 그 한 줄
    /// 규약에 기대고 있다. 사본이 생기면 그중 하나만 고쳐지는 날이 온다.
    public sealed class JsonMapWriter : IMapWriter
    {
        public string Extension => ".json";

        public string Describe => "JSON (서버가 읽는 형식)";

        public bool IsText => true;

        public void Write(StringBuilder into, MapData data)
        {
            into.Append(MapExportPipeline.Serialize(data));
        }
    }
}
