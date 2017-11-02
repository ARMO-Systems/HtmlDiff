using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ArmoSystems.ArmoGet.HtmlDiff
{
    public class HtmlDiff
    {
        /// <summary>
        ///   Initializes a new instance of the
        ///   <see>
        ///     <cref>Diff</cref>
        ///   </see>
        ///   class.
        /// </summary>
        /// <param name="oldText">The old text.</param>
        /// <param name="newText">The new text.</param>
        public HtmlDiff( string oldText, string newText )
        {
            this.oldText = oldText;
            this.newText = newText;

            content = new StringBuilder();
        }

        private readonly StringBuilder content;
        private readonly string newText;
        private readonly string oldText;
        private readonly string[] specialCaseClosingTags = { "</strong>", "</b>", "</i>", "</big>", "</small>", "</u>", "</sub>", "</sup>", "</strike>", "</s>" };

        private readonly string[] specialCaseOpeningTags =
        {
            "<strong[\\>\\s]+",
            "<b[\\>\\s]+",
            "<i[\\>\\s]+",
            "<big[\\>\\s]+",
            "<small[\\>\\s]+",
            "<u[\\>\\s]+",
            "<sub[\\>\\s]+",
            "<sup[\\>\\s]+",
            "<strike[\\>\\s]+",
            "<s[\\>\\s]+"
        };

        private List< Operation > computedOperations;

        private string[] newWords;
        private string[] oldWords;
        private Dictionary< string, List< int > > wordIndices;

        /// <summary>
        ///   Compute differn between two files
        /// </summary>
        /// <returns>Contains different</returns>
        public bool ComputeDiff()
        {
            SplitInputsToWords();

            IndexNewWords();

            computedOperations = Operations();
            return computedOperations.Any( item => item.Action != Action.Equal );
        }

        /// <summary>
        ///   Builds the HTML diff output
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <returns>HTML diff markup</returns>
        public string BuildDiffPage()
        {
            if ( computedOperations == null )
                // ReSharper disable once NotResolvedInText
                throw new ArgumentNullException( "computedOperations", "Call ComputeDiff function before call Build" );

            foreach ( var item in Operations() )
                PerformOperation( item );

            return content.ToString();
        }

        private void IndexNewWords()
        {
            wordIndices = new Dictionary< string, List< int > >();
            for ( var i = 0; i < newWords.Length; i++ )
            {
                var word = newWords[ i ];

                if ( wordIndices.ContainsKey( word ) )
                    wordIndices[ word ].Add( i );
                else
                    wordIndices[ word ] = new List< int > { i };
            }
        }

        private void SplitInputsToWords()
        {
            oldWords = ConvertHtmlToListOfWords( Explode( oldText ) );
            newWords = ConvertHtmlToListOfWords( Explode( newText ) );
        }

        private static string[] ConvertHtmlToListOfWords( IEnumerable< string > characterString )
        {
            var mode = Mode.Character;
            var currentWord = string.Empty;
            var words = new List< string >();

            foreach ( var character in characterString )
            {
                switch ( mode )
                {
                    case Mode.Character:

                        if ( IsStartOfTag( character ) )
                        {
                            if ( currentWord != string.Empty )
                                words.Add( currentWord );

                            currentWord = "<";
                            mode = Mode.Tag;
                        }
                        else if ( Regex.IsMatch( character, "\\s" ) )
                        {
                            if ( currentWord != string.Empty )
                                words.Add( currentWord );
                            currentWord = character;
                            mode = Mode.Whitespace;
                        }
                        else
                            currentWord += character;

                        break;
                    case Mode.Tag:

                        if ( IsEndOfTag( character ) )
                        {
                            currentWord += ">";
                            words.Add( currentWord );
                            currentWord = "";

                            mode = IsWhiteSpace( character ) ? Mode.Whitespace : Mode.Character;
                        }
                        else
                            currentWord += character;

                        break;
                    case Mode.Whitespace:

                        if ( IsStartOfTag( character ) )
                        {
                            if ( currentWord != string.Empty )
                                words.Add( currentWord );
                            currentWord = "<";
                            mode = Mode.Tag;
                        }
                        else if ( Regex.IsMatch( character, "\\s" ) )
                            currentWord += character;
                        else
                        {
                            if ( currentWord != string.Empty )
                                words.Add( currentWord );

                            currentWord = character;
                            mode = Mode.Character;
                        }

                        break;
                }
            }
            if ( currentWord != string.Empty )
                words.Add( currentWord );

            return words.ToArray();
        }

        private static bool IsStartOfTag( string val )
        {
            return val == "<";
        }

        private static bool IsEndOfTag( string val )
        {
            return val == ">";
        }

        private static bool IsWhiteSpace( string value )
        {
            return Regex.IsMatch( value, "\\s" );
        }

        private static IEnumerable< string > Explode( string value )
        {
            return Regex.Split( value, "" );
        }

        private void PerformOperation( Operation operation )
        {
            switch ( operation.Action )
            {
                case Action.Equal:
                    ProcessEqualOperation( operation );
                    break;
                case Action.Delete:
                    ProcessDeleteOperation( operation, "diffdel" );
                    break;
                case Action.Insert:
                    ProcessInsertOperation( operation, "diffins" );
                    break;
                case Action.None:
                    break;
                case Action.Replace:
                    ProcessReplaceOperation( operation );
                    break;
            }
        }

        private void ProcessReplaceOperation( Operation operation )
        {
            ProcessDeleteOperation( operation, "diffmod" );
            ProcessInsertOperation( operation, "diffmod" );
        }

        private void ProcessInsertOperation( Operation operation, string cssClass )
        {
            InsertTag( "ins", cssClass, newWords.Where( ( s, pos ) => pos >= operation.StartInNew && pos < operation.EndInNew ).ToList() );
        }

        private void ProcessDeleteOperation( Operation operation, string cssClass )
        {
            var text = oldWords.Where( ( s, pos ) => pos >= operation.StartInOld && pos < operation.EndInOld ).ToList();
            InsertTag( "del", cssClass, text );
        }

        private void ProcessEqualOperation( Operation operation )
        {
            var result = newWords.Where( ( s, pos ) => pos >= operation.StartInNew && pos < operation.EndInNew ).ToArray();
            content.Append( string.Join( "", result ) );
        }

        /// <summary>
        ///   This method encloses words within a specified tag (ins or del), and adds this into "content",
        ///   with a twist: if there are words contain tags, it actually creates multiple ins or del,
        ///   so that they don't include any ins or del. This handles cases like
        ///   old: '<p>a</p>'
        ///   new: '<p>ab</p><p />c'
        ///   diff result: '<p>a<ins>b</ins></p>
        ///   <p>
        ///     <ins>c</ins>
        ///   </p>
        ///   '
        ///   this still doesn't guarantee valid HTML (hint: think about diffing a text containing ins or
        ///   del tags), but handles correctly more cases than the earlier version.
        ///   P.S.: Spare a thought for people who write HTML browsers. They live in this ... every day.
        /// </summary>
        /// <param name="tag"></param>
        /// <param name="cssClass"></param>
        /// <param name="words"></param>
        private void InsertTag( string tag, string cssClass, List< string > words )
        {
            while ( true )
            {
                if ( words.Count == 0 )
                    break;

                var nonTags = ExtractConsecutiveWords( words, x => !IsTag( x ) );

                var specialCaseTagInjection = string.Empty;
                var specialCaseTagInjectionIsBefore = false;

                if ( nonTags.Length != 0 )
                {
                    var text = WrapText( string.Join( "", nonTags ), tag, cssClass );

                    content.Append( text );
                }
                else
                {
                    // Check if strong tag

                    if ( specialCaseOpeningTags.FirstOrDefault( x => Regex.IsMatch( words[ 0 ], x ) ) != null )
                    {
                        specialCaseTagInjection = "<ins class='mod'>";
                        if ( tag == "del" )
                            words.RemoveAt( 0 );
                    }
                    else if ( specialCaseClosingTags.Contains( words[ 0 ] ) )
                    {
                        specialCaseTagInjection = "</ins>";
                        specialCaseTagInjectionIsBefore = true;
                        if ( tag == "del" )
                            words.RemoveAt( 0 );
                    }
                }

                if ( words.Count == 0 && specialCaseTagInjection.Length == 0 )
                    break;

                if ( specialCaseTagInjectionIsBefore )
                    content.Append( specialCaseTagInjection + string.Join( "", ExtractConsecutiveWords( words, IsTag ) ) );
                else
                    content.Append( string.Join( "", ExtractConsecutiveWords( words, IsTag ) ) + specialCaseTagInjection );
            }
        }

        private static string WrapText( string text, string tagName, string cssClass )
        {
            return string.Format( "<{0} class='{1}'>{2}</{0}>", tagName, cssClass, text );
        }

        private static string[] ExtractConsecutiveWords( List< string > words, Func< string, bool > condition )
        {
            int? indexOfFirstTag = null;

            for ( var i = 0; i < words.Count; i++ )
            {
                var word = words[ i ];

                if ( condition( word ) )
                    continue;
                indexOfFirstTag = i;
                break;
            }

            if ( indexOfFirstTag != null )
            {
                var items = words.Where( ( s, pos ) => pos >= 0 && pos < indexOfFirstTag ).ToArray();
                if ( indexOfFirstTag.Value > 0 )
                    words.RemoveRange( 0, indexOfFirstTag.Value );
                return items;
            }
            else
            {
                var items = words.Where( ( s, pos ) => pos >= 0 && pos <= words.Count ).ToArray();
                words.RemoveRange( 0, words.Count );
                return items;
            }
        }

        private static bool IsTag( string item )
        {
            var isTag = IsOpeningTag( item ) || IsClosingTag( item );
            return isTag;
        }

        private static bool IsOpeningTag( string item )
        {
            return Regex.IsMatch( item, "^\\s*<[^>]+>\\s*$" );
        }

        private static bool IsClosingTag( string item )
        {
            return Regex.IsMatch( item, "^\\s*</[^>]+>\\s*$" );
        }

        private List< Operation > Operations()
        {
            int positionInOld = 0, positionInNew = 0;
            var operations = new List< Operation >();

            var matches = MatchingBlocks();

            matches.Add( new Match( oldWords.Length, newWords.Length, 0 ) );

            foreach ( var match in matches )
            {
                var matchStartsAtCurrentPositionInOld = positionInOld == match.StartInOld;
                var matchStartsAtCurrentPositionInNew = positionInNew == match.StartInNew;

                Action action;

                if ( matchStartsAtCurrentPositionInOld == false && matchStartsAtCurrentPositionInNew == false )
                    action = Action.Replace;
                else if ( matchStartsAtCurrentPositionInOld && matchStartsAtCurrentPositionInNew == false )
                    action = Action.Insert;
                else if ( matchStartsAtCurrentPositionInOld == false )
                    action = Action.Delete;
                else // This occurs if the first few words are the same in both versions
                    action = Action.None;

                if ( action != Action.None )
                    operations.Add( new Operation( action, positionInOld, match.StartInOld, positionInNew, match.StartInNew ) );

                if ( match.Size != 0 )
                    operations.Add( new Operation( Action.Equal, match.StartInOld, match.EndInOld, match.StartInNew, match.EndInNew ) );

                positionInOld = match.EndInOld;
                positionInNew = match.EndInNew;
            }

            return operations;
        }

        private List< Match > MatchingBlocks()
        {
            var matchingBlocks = new List< Match >();
            FindMatchingBlocks( 0, oldWords.Length, 0, newWords.Length, matchingBlocks );
            return matchingBlocks;
        }

        private void FindMatchingBlocks( int startInOld, int endInOld, int startInNew, int endInNew, ICollection< Match > matchingBlocks )
        {
            while ( true )
            {
                var match = FindMatch( startInOld, endInOld, startInNew, endInNew );

                if ( match == null )
                    return;
                if ( startInOld < match.StartInOld && startInNew < match.StartInNew )
                    FindMatchingBlocks( startInOld, match.StartInOld, startInNew, match.StartInNew, matchingBlocks );

                matchingBlocks.Add( match );

                if ( match.EndInOld < endInOld && match.EndInNew < endInNew )
                {
                    startInOld = match.EndInOld;
                    startInNew = match.EndInNew;
                    continue;
                }
                break;
            }
        }

        private Match FindMatch( int startInOld, int endInOld, int startInNew, int endInNew )
        {
            var bestMatchInOld = startInOld;
            var bestMatchInNew = startInNew;
            var bestMatchSize = 0;

            var matchLengthAt = new Dictionary< int, int >();

            for ( var indexInOld = startInOld; indexInOld < endInOld; indexInOld++ )
            {
                var newMatchLengthAt = new Dictionary< int, int >();

                var index = oldWords[ indexInOld ];

                if ( !wordIndices.ContainsKey( index ) )
                {
                    matchLengthAt = newMatchLengthAt;
                    continue;
                }

                foreach ( var indexInNew in wordIndices[ index ] )
                {
                    if ( indexInNew < startInNew )
                        continue;

                    if ( indexInNew >= endInNew )
                        break;

                    var newMatchLength = ( matchLengthAt.ContainsKey( indexInNew - 1 ) ? matchLengthAt[ indexInNew - 1 ] : 0 ) + 1;
                    newMatchLengthAt[ indexInNew ] = newMatchLength;

                    if ( newMatchLength <= bestMatchSize )
                        continue;
                    bestMatchInOld = indexInOld - newMatchLength + 1;
                    bestMatchInNew = indexInNew - newMatchLength + 1;
                    bestMatchSize = newMatchLength;
                }

                matchLengthAt = newMatchLengthAt;
            }

            return bestMatchSize != 0 ? new Match( bestMatchInOld, bestMatchInNew, bestMatchSize ) : null;
        }
    }

    internal sealed class Match
    {
        public Match( int startInOld, int startInNew, int size )
        {
            StartInOld = startInOld;
            StartInNew = startInNew;
            Size = size;
        }

        public int StartInOld { get; set; }
        public int StartInNew { get; set; }
        public int Size { get; set; }

        public int EndInOld
        {
            get { return StartInOld + Size; }
        }

        public int EndInNew
        {
            get { return StartInNew + Size; }
        }
    }

    internal sealed class Operation
    {
        public Operation( Action action, int startInOld, int endInOld, int startInNew, int endInNew )
        {
            Action = action;
            StartInOld = startInOld;
            EndInOld = endInOld;
            StartInNew = startInNew;
            EndInNew = endInNew;
        }

        internal Action Action { get; set; }
        internal int StartInOld { get; set; }
        internal int EndInOld { get; set; }
        internal int StartInNew { get; set; }
        internal int EndInNew { get; set; }
    }

    internal enum Mode
    {
        Character,
        Tag,
        Whitespace
    }

    internal enum Action
    {
        Equal,
        Delete,
        Insert,
        None,
        Replace
    }
}