using JetBrains.Annotations;

namespace Traydio.Views;

public sealed record CommandTipItem([IgnoreSpellingAndGrammarErrors] string Command, string Tooltip);

