namespace AkademVault_API.Services;

// Cryptographically random short-code generator for human-shareable group identifiers.
public interface IShortCodeGenerator
{
    // Returns a fresh formatted code such as "ABCD-1234".
    string Generate();
}

// Concrete generator: pulls 8 random bytes and maps each into an unambiguous-character alphabet.
public class ShortCodeGenerator : IShortCodeGenerator
{
    // Crockford-style alphabet without 0/1/I/O to avoid visual ambiguity in shared codes.
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
