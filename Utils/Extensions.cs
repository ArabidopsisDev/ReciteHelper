namespace ReciteHelper.Utils;

public static class Extensions
{
    extension(string str)
    {
        public char FindNextChar(int index)
        {
            for (int i = index; i < str.Length; i++)
                if (!char.IsWhiteSpace(str[i])) return str[i];
            return ' ';
        }
    }
}
