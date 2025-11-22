namespace ReciteHelper.Utils;

public class FixJson
{
    public string CompleteBracket(string originJson)
    {
        var series = new Stack<char>();
        var jsonString = originJson;

        foreach (var token in originJson)
        {
            switch (token)
            {
                case '{' or '[':
                    series.Push(token);
                    break;
                case '}' or ']':
                    series.Pop();
                    break;
                default:
                    break;
            }
        }

        while (series.Count != 0)
        {
            var token = series.Pop();
            jsonString += '\n' + token switch
            {
                '[' => ']',
                '{' => '}',
                _ => throw new ArgumentException("The JSON file is corrupted!")
            };
        }

        return jsonString;
    }
}
