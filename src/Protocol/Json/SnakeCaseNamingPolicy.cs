using System.Text;
using System.Text.Json;

namespace SdvTestFramework.Protocol.Json;

/// <summary>
/// PascalCase → snake_case naming policy for <see cref="JsonSerializerOptions.PropertyNamingPolicy"/>.
/// .NET 8 ships <c>JsonNamingPolicy.SnakeCaseLower</c>; this reimplementation is portable
/// back to our net6.0 floor.
/// </summary>
/// <remarks>
/// Rules: insert an underscore before each interior uppercase letter, then lowercase the
/// whole string. Runs of uppercase are kept together ("HTTPServer" → "http_server").
/// </remarks>
public sealed class SnakeCaseNamingPolicy : JsonNamingPolicy
{
    public static readonly SnakeCaseNamingPolicy Instance = new();

    public override string ConvertName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            bool isUpper = c is >= 'A' and <= 'Z';
            if (i > 0 && isUpper)
            {
                char prev = name[i - 1];
                char? next = i + 1 < name.Length ? name[i + 1] : null;

                bool prevIsLower = prev is >= 'a' and <= 'z';
                bool nextIsLower = next is >= 'a' and <= 'z';

                // Insert `_` at word boundaries:
                //   (a) lowercase-then-uppercase: "maxStamina" → "max_Stamina"
                //   (b) end of uppercase run before a lowercase:  "HTTPServer" → "HTTP_Server"
                if (prevIsLower || nextIsLower)
                    sb.Append('_');
            }
            sb.Append(isUpper ? (char)(c + 32) : c);
        }
        return sb.ToString();
    }
}
