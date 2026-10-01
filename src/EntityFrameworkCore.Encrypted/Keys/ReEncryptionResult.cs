namespace EntityFrameworkCore.Encrypted.Keys;

/// <summary>Outcome of re-encrypting the stored values of a context.</summary>
/// <param name="ReEncrypted">Values re-encrypted with the active key.</param>
/// <param name="Skipped">Values changed by the application while re-encrypting: already encrypted with the active key.</param>
/// <param name="Invalid">Values that can't be decrypted (e.g. plaintext, or their root key is missing); left as is.</param>
public sealed record ReEncryptionResult(long ReEncrypted, long Skipped, long Invalid);
