using System.Globalization;

namespace Mt.Authoring.Graph;

/// <summary>
/// 인스펙터 칸의 상대 입력(마야 채널 박스와 같다): <c>+=3</c> · <c>-=0.5</c> · <c>*=2</c> · <c>/=2</c>. 그냥 숫자는 절대값.
/// 여러 키가 선택돼 있으면 각 키의 값에 같은 식을 적용한다.
/// </summary>
public static class RelativeInput
{
    public static bool TryParse(string text, out Func<float, float> apply)
    {
        apply = v => v;
        string trimmed = (text ?? "").Trim();

        if (trimmed.Length >= 3 && trimmed[1] == '=' && "+-*/".Contains(trimmed[0]))
        {
            if (!float.TryParse(trimmed[2..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float operand))
            {
                return false;
            }

            char op = trimmed[0];
            apply = op switch
            {
                '+' => v => v + operand,
                '-' => v => v - operand,
                '*' => v => v * operand,
                _ => v => Math.Abs(operand) < 1e-12f ? v : v / operand,
            };
            return true;
        }

        if (!float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float absolute))
        {
            return false;
        }

        apply = _ => absolute;
        return true;
    }
}
