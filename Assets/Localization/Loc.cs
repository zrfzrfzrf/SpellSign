using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Every text the player reads, in the language picked with the flags on the
// start menu. The choice is remembered across restarts (PlayerPrefs).
//
// The words live in Resources/Translations.csv, which opens in Excel or
// Google Sheets: one row per text, the first column its key, then one column
// per language ("en", "sv", ...), and a last "note" column saying where the
// text shows. {0}, {1} in a text are filled in by the game (an element name,
// a shape). A text missing in a language shows in English.
//
// Adding a language: add its column to the sheet and a flag (Flags.cs).
public static class Loc
{
    public const string English = "en";
    const string PrefsKey = "SpellSign.Language";
    const string NoteColumn = "note";

    // Tells what shows text to rewrite it in the new language.
    public static event Action Changed;

    static Dictionary<string, Dictionary<string, string>> texts; // language -> key -> text
    static List<string> languages;
    static string current;
    static readonly HashSet<string> reportedMissing = new HashSet<string>();

    // Read again on each play in the editor (also with domain reload off), so sheet edits show.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        texts = null;
        Changed = null;
        reportedMissing.Clear();
    }

    // The languages in the sheet, in its column order.
    public static IReadOnlyList<string> Languages
    {
        get
        {
            Load();
            return languages;
        }
    }

    public static string Current
    {
        get
        {
            Load();
            return current;
        }
    }

    public static void Set(string language)
    {
        Load();
        if (language == current || !texts.ContainsKey(language)) return;
        current = language;
        PlayerPrefs.SetString(PrefsKey, language);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    // The text for this key in the current language.
    public static string T(string key)
    {
        Load();
        if (Find(current, key, out string text) || Find(English, key, out text)) return text;

        if (reportedMissing.Add(key)) Debug.LogWarning("[Loc] no text for \"" + key + "\" in Translations.csv");
        return key;
    }

    // ...with {0}, {1} filled in.
    public static string T(string key, params object[] args)
    {
        string text = T(key);
        try
        {
            return string.Format(text, args);
        }
        catch (FormatException)
        {
            if (reportedMissing.Add(key + "/format")) Debug.LogWarning("[Loc] broken {0}/{1} in \"" + key + "\": " + text);
            return text;
        }
    }

    static bool Find(string language, string key, out string text)
    {
        text = null;
        return texts.TryGetValue(language, out Dictionary<string, string> table)
               && table.TryGetValue(key, out text) && !string.IsNullOrEmpty(text);
    }

    static void Load()
    {
        if (texts != null) return;

        texts = new Dictionary<string, Dictionary<string, string>>();
        languages = new List<string>();

        var sheet = Resources.Load<TextAsset>("Translations");
        if (sheet == null) Debug.LogError("[Loc] Resources/Translations.csv not found -- texts show as their keys.");
        else Read(sheet.text);

        if (!texts.ContainsKey(English))
        {
            texts[English] = new Dictionary<string, string>();
            languages.Insert(0, English);
        }

        string saved = PlayerPrefs.GetString(PrefsKey, English);
        current = texts.ContainsKey(saved) ? saved : English;
    }

    static void Read(string csv)
    {
        List<List<string>> rows = ParseCsv(csv);
        if (rows.Count == 0) return;

        List<string> header = rows[0];
        var columns = new Dictionary<int, string>(); // column -> language
        for (int c = 1; c < header.Count; c++)
        {
            string language = header[c].Trim();
            if (language.Length == 0 || language.Equals(NoteColumn, StringComparison.OrdinalIgnoreCase)) continue;
            columns[c] = language;
            languages.Add(language);
            texts[language] = new Dictionary<string, string>();
        }

        for (int r = 1; r < rows.Count; r++)
        {
            List<string> row = rows[r];
            string key = row[0].Trim();
            if (key.Length == 0) continue;
            foreach (KeyValuePair<int, string> column in columns)
                if (column.Key < row.Count) texts[column.Value][key] = row[column.Key];
        }
    }

    // Comma-separated, or semicolon-separated as Excel saves it with Swedish
    // settings. Quoted fields may hold separators, line breaks and "" quotes.
    static List<List<string>> ParseCsv(string csv)
    {
        csv = csv.TrimStart('﻿');
        int firstLineEnd = csv.IndexOf('\n');
        string firstLine = firstLineEnd < 0 ? csv : csv.Substring(0, firstLineEnd);
        char separator = firstLine.Split(';').Length > firstLine.Split(',').Length ? ';' : ',';

        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < csv.Length; i++)
        {
            char ch = csv[i];
            if (quoted)
            {
                if (ch != '"') field.Append(ch);
                else if (i + 1 < csv.Length && csv[i + 1] == '"') field.Append(csv[++i]);
                else quoted = false;
            }
            else if (ch == '"') quoted = true;
            else if (ch == separator)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else field.Append(ch);
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }
}
