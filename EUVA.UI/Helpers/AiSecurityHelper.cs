// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Security.Cryptography;
using System.Text;

namespace EUVA.UI.Helpers;

public static class AiSecurityHelper
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("EUVA_AI_Settings_v1");

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(plainText);
            byte[] encrypted = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string Decrypt(string encryptedBase64)
    {
        if (string.IsNullOrEmpty(encryptedBase64)) return string.Empty;
        try
        {
            byte[] encrypted = Convert.FromBase64String(encryptedBase64);
            byte[] decrypted = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return string.Empty;
        }
    }
}
