using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace GangaKiBagiya
{
    public enum PlayerGender
    {
        Unspecified = 0,
        Male = 1,
        Female = 2,
        Other = 3,
        PreferNotToSay = 4
    }

    /// <summary>Local-only player profile. No account, e-mail, phone or password is ever collected.</summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        public const int AvatarCount = 6;
        public const int NameMinLength = 2;
        public const int NameMaxLength = 20;
        public const int AgeMin = 5;
        public const int AgeMax = 120;

        public int schemaVersion = 1;
        public string name = string.Empty;
        public PlayerGender gender = PlayerGender.Unspecified;
        public int age;
        public int avatarIndex;
        public string createdUtc = string.Empty;
        public bool completed;

        public bool IsValid()
        {
            return schemaVersion == 1 && completed && PlayerProfileValidation.TryName(name, out string cleaned, out _) &&
                   cleaned == name && Enum.IsDefined(typeof(PlayerGender), gender) &&
                   (age == 0 || (age >= AgeMin && age <= AgeMax)) && avatarIndex >= 0 && avatarIndex < AvatarCount;
        }
    }

    /// <summary>Validation shared by the form and the store. Error values are localization keys.</summary>
    public static class PlayerProfileValidation
    {
        private static readonly Regex NameShape = new Regex(@"^[\p{L}\p{M}][\p{L}\p{M} .'\-]*$", RegexOptions.Compiled);
        private static readonly Regex Spaces = new Regex(@" {2,}", RegexOptions.Compiled);

        public static bool TryName(string input, out string cleaned, out string errorKey)
        {
            cleaned = Spaces.Replace((input ?? string.Empty).Trim(), " ");
            if (cleaned.Length == 0)
            {
                errorKey = "profile.err.nameEmpty";
                return false;
            }
            if (cleaned.Length < PlayerProfile.NameMinLength || cleaned.Length > PlayerProfile.NameMaxLength)
            {
                errorKey = "profile.err.nameLength";
                return false;
            }
            if (!NameShape.IsMatch(cleaned))
            {
                errorKey = "profile.err.nameChars";
                return false;
            }
            errorKey = null;
            return true;
        }

        /// <summary>Age is optional: empty input yields 0 (not given).</summary>
        public static bool TryAge(string input, out int age, out string errorKey)
        {
            age = 0;
            errorKey = null;
            string text = (input ?? string.Empty).Trim();
            if (text.Length == 0)
                return true;
            if (text.Length > 3 || !int.TryParse(text, out age) || age < PlayerProfile.AgeMin || age > PlayerProfile.AgeMax)
            {
                age = 0;
                errorKey = "profile.err.age";
                return false;
            }
            return true;
        }
    }

    /// <summary>Atomic JSON persistence for the profile. Never throws; never logs profile contents.</summary>
    public sealed class PlayerProfileStore
    {
        public const string FileName = "player-profile-v1.json";
        private readonly string path;

        public PlayerProfileStore(string directory)
        {
            path = Path.Combine(directory, FileName);
        }

        public string FilePath => path;

        /// <summary>Returns the saved profile, or null when none exists or the file was unusable (<paramref name="damaged"/>).</summary>
        public PlayerProfile Load(out bool damaged)
        {
            damaged = false;
            if (!File.Exists(path))
                return null;
            try
            {
                PlayerProfile profile = JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(path));
                if (profile != null && profile.IsValid())
                    return profile;
            }
            catch (Exception)
            {
            }

            damaged = true;
            try
            {
                string aside = path + ".corrupt";
                if (File.Exists(aside))
                    File.Delete(aside);
                File.Move(path, aside);
            }
            catch (Exception)
            {
            }
            return null;
        }

        public bool Save(PlayerProfile profile)
        {
            if (profile == null || !profile.IsValid())
                return false;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(profile));
                if (File.Exists(path))
                    File.Replace(temp, path, null);
                else
                    File.Move(temp, path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
