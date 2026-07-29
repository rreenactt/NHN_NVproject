using System.Text.Json;
using System.Text.Json.Serialization;

namespace NV.Infrastructure.Json
{
    /// 직렬화 기본 옵션.
    ///
    /// Shared 의 DTO 에는 [JsonPropertyName] 을 붙일 수 없다. System.Text.Json 이
    /// NuGet 이고 Unity 가 그 어셈블리를 갖지 않는다. 명명 규칙을 여기서 맞춘다.
    ///
    /// JSON 필드는 camelCase 다. 클라이언트가 export 하는 맵 파일도 같은 규칙을 쓴다.
    public static class JsonDefaults
    {
        public static readonly JsonSerializerOptions Options = Create();

        private static JsonSerializerOptions Create()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,

                // 맵 파일은 손으로도 고치므로 주석과 꼬리 콤마를 허용한다.
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                WriteIndented = false,
            };

            return options;
        }
    }
}
