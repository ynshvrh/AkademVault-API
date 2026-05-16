namespace AkademVault_API.Services;

public interface IShortCodeGenerator
{
    string Generate();
}

public class ShortCodeGenerator : IShortCodeGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public string Generate()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
        var chars = new char[8];
        for (int i = 0; i < 8; i++)
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];

        return $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}";
    }
}
