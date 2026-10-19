using System;

namespace Ryujinx.Common
{
    /// <summary>
    /// [Nextendo] Comparaison des versions de jeu (chaine d'affichage de la NACP, ex. "4.0.1",
    /// "1.0.17", "13.0.5"). La version de la table d'ApplicationData est la version EXACTE
    /// obligatoire en ligne : ni une plus ancienne, ni une plus recente (une mise a jour Nintendo
    /// que Nextendo n'a pas encore preparee ne doit pas pouvoir aller en ligne). La lecture
    /// numerique sert seulement a normaliser l'ecriture ("4.0.1" == "4.0.1.0" == "v4.0.1").
    /// </summary>
    public static class NextendoGameVersion
    {
        /// <summary>
        /// Vrai si <paramref name="installed"/> est exactement <paramref name="required"/>
        /// (egalite numerique, composants absents = 0). Une version illisible (vide, texte, plus
        /// de 4 composants) ne passe que si elle est identique au texte exige.
        /// </summary>
        public static bool IsExactly(string installed, string required)
        {
            if (string.IsNullOrWhiteSpace(installed) || string.IsNullOrWhiteSpace(required))
            {
                return false;
            }

            if (string.Equals(installed.Trim(), required.Trim(), StringComparison.Ordinal))
            {
                return true;
            }

            return TryParse(installed, out Version have) && TryParse(required, out Version need) && have == need;
        }

        /// <summary>
        /// Lit une version de jeu en 1 a 4 composants numeriques ("v" initial tolere). Les
        /// composants absents valent 0, donc "4.0" == "4.0.0" == "4.0.0.0".
        /// </summary>
        public static bool TryParse(string text, out Version version)
        {
            version = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string s = text.Trim();
            if (s.Length > 1 && (s[0] == 'v' || s[0] == 'V'))
            {
                s = s[1..];
            }

            string[] parts = s.Split('.');
            if (parts.Length is < 1 or > 4)
            {
                return false;
            }

            int[] values = new int[4];
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.Length == 0 || p.Length > 9)
                {
                    return false;
                }

                foreach (char c in p)
                {
                    if (c is < '0' or > '9')
                    {
                        return false;
                    }
                }

                values[i] = int.Parse(p, System.Globalization.CultureInfo.InvariantCulture);
            }

            version = new Version(values[0], values[1], values[2], values[3]);

            return true;
        }
    }
}
