namespace NetPack.Html;

using System;

public ref struct HtmlTokenizer
{
    private ReadOnlySpan<char> _content;
    private int _position;
    private HtmlParseMode _state = HtmlParseMode.PCData;

    public HtmlTokenizer(ReadOnlySpan<char> content)
    {
        _content = content;
        _position = 0;
    }

    public readonly bool IsActive => _position < _content.Length;

    public Token NextToken()
    {
        if (IsActive)
        {
            switch (_state)
            {
                case HtmlParseMode.PCData:
                    return Data();
                case HtmlParseMode.RCData:
                    return RCData();
                case HtmlParseMode.Plaintext:
                    return Plaintext();
                case HtmlParseMode.Rawtext:
                    return Rawtext();
                case HtmlParseMode.Script:
                    return ScriptData();
            }
        }

        return new Token(TokenType.Unknown, []);
    }

    #region Data

    private Token Data()
    {
        if (_content[_position] == '<')
        {
            return TagOpen();
        }

        return DataText();
    }

    private Token DataText()
    {
        var start = _position;

        while (IsActive)
        {
            if (GetNext() == '<')
            {
                break;
            }
        }

        return NewCharacter(start, _position);
    }

    #endregion

    #region Plaintext

    private readonly Token Plaintext()
    {
        return NewCharacter(_position, _content.Length);
    }

    #endregion

    #region RCData

    private Token RCData()
    {
        var start = _position;
        var c = _content[start];

        if (c == '<')
        {
            c = GetNext();

            if (c == '/')
            {
                c = GetNext();

                if (IsLetter(c))
                {
                    c = GetNext();

                    while (IsActive)
                    {
                        if (c == '>')
                        {
                            Advance();
                            return NewTagClose(start, _position);
                        }
                        else if (!IsLetter(c))
                        {
                            ConsumeRCDataText();
                            break;
                        }

                        c = GetNext();
                    }
                }
                else
                {
                    ConsumeRCDataText();
                }
            }
            else
            {
                ConsumeRCDataText();
            }
        }

        return NewCharacter(start, _position);
    }

    private void ConsumeRCDataText()
    {
        while (IsActive)
        {
            var c = _content[_position++];

            if (c == '<')
            {
                return;
            }
        }
    }

    #endregion

    #region Rawtext

    private Token Rawtext()
    {
        var start = _position;
        var c = _content[_position];

        if (c == '<')
        {
            c = GetNext();

            if (c == '/')
            {
                c = GetNext();

                if (IsLetter(c))
                {
                    while (IsActive)
                    {
                        if (c == '>')
                        {
                            Advance();
                            return NewTagClose(start, _position);
                        }
                        else if (!IsLetter(c))
                        {
                            break;
                        }

                        c = GetNext();
                    }
                }
            }
        }

        ConsumeRawtextText();
        return NewCharacter(start, _position);
    }

    private void ConsumeRawtextText()
    {
        while (IsActive)
        {
            var c = _content[_position++];

            if (c == '<')
            {
                break;
            }
        }
    }

    #endregion

    #region CDATA

    private Token CharacterData()
    {
        var start = _position;

        while (IsActive)
        {
            var c = _content[_position++];

            if (c == ']' && ContinuesWithSensitive("]>"))
            {
                Advance(2);
                break;
            }
        }

        return NewCharacter(start, _position);
    }

    #endregion

    #region Tags

    private Token TagOpen()
    {
        var start = _position;
        var c = GetNext();

        if (c == '/')
        {
            Back();
            return TagEnd();
        }
        else if (IsLetter(c))
        {
            ConsumeTagName();
            return NewTagOpen(start, _position);
        }
        else if (c == '?')
        {
            Back();
            return ProcessingInstruction();
        }
        else if (ContinuesWithSensitive("!--"))
        {
            Back();
            return CommentStart();
        }
        else if (ContinuesWithInsensitive("!DOCTYPE"))
        {
            Back();
            return Doctype();
        }
        else if (ContinuesWithSensitive("![CDATA["))
        {
            Back();
            return CharacterData();
        }
        else if (c == '!')
        {
            Back();
            return BogusComment();
        }
        else
        {
            _state = HtmlParseMode.PCData;
            return DataText();
        }
    }

    private Token TagEnd()
    {
        var start = _position;
        Advance(2);

        if (!IsActive)
        {
            return NewCharacter(start, _position);
        }

        var c = _content[_position];

        if (IsLetter(c))
        {
            ConsumeTagName();
            return NewTagClose(start, _position);
        }
        else if (c == '>')
        {
            _state = HtmlParseMode.PCData;
            Advance();
            return Data();
        }
        else
        {
            _position = start;
            return BogusComment();
        }
    }

    private void ConsumeTagName()
    {
        while (IsActive)
        {
            var c = GetNext();

            if (c == '>')
            {
                Advance();
                _state = HtmlParseMode.PCData;
                return;
            }
            else if (IsSpaceCharacter(c))
            {
                ConsumeAttributes();
                return;
            }
            else if (c == '/')
            {
                Advance();
                ConsumeTagSelfClosing();
                return;
            }
        }
    }

    private void ConsumeTagSelfClosing()
    {
        if (!IsTagSelfClosingInner())
        {
            ConsumeAttributes();
        }
    }

    private bool IsTagSelfClosingInner()
    {
        while (IsActive)
        {
            switch (GetNext())
            {
                case '>':
                    Advance();
                    _state = HtmlParseMode.PCData;
                    return true;
                case '/':
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    #endregion

    #region ProcessingInstructions

    private Token ProcessingInstruction()
    {
        var start = _position;

        while (IsActive)
        {
            var c = _content[_position++];

            if (c == '>')
            {
                break;
            }
        }

        _state = HtmlParseMode.PCData;
        return NewProcessingInstruction(start, _position);
    }

    #endregion

    #region Comments

    private Token BogusComment()
    {
        var start = _position;

        while (IsActive)
        {
            var c = _content[_position++];

            if (c == '>')
            {
                break;
            }
        }

        _state = HtmlParseMode.PCData;
        return NewComment(start, _position);
    }

    private Token CommentStart()
    {
        var start = _position;
        Advance(4);

        if (IsActive)
        {
            var c = _content[_position];

            switch (c)
            {
                case '-':
                    Advance();

                    if (!IsCommentDashStart())
                    {
                        goto default;
                    }

                    break;
                case '>':
                    _state = HtmlParseMode.PCData;
                    Advance();
                    break;
                default:
                    ConsumeComment();
                    break;
            }
        }


        return NewComment(start, _position);
    }

    private bool IsCommentDashStart()
    {
        if (IsActive)
        {
            var c = _content[_position];

            switch (c)
            {
                case '-':
                    Advance();
                    return IsCommentEnd();
                case '>':
                    _state = HtmlParseMode.PCData;
                    Advance();
                    break;
                default:
                    ConsumeComment();
                    break;
            }
        }

        return true;
    }

    private void ConsumeComment()
    {
        while (IsActive)
        {
            var c = _content[_position++];

            if (c == '-' && IsCommentDashEnd())
            {
                return;
            }
        }
    }

    private bool IsCommentDashEnd()
    {
        if (IsActive)
        {
            var c = _content[_position];

            if (c == '-')
            {
                Advance();
                return IsCommentEnd();
            }

            return false;
        }

        return true;
    }

    private bool IsCommentEnd()
    {
        while (IsActive)
        {
            var c = _content[_position++];

            switch (c)
            {
                case '>':
                    _state = HtmlParseMode.PCData;
                    return true;
                case '!':
                    Advance();
                    return IsCommentBangEnd();
                case '-':
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private bool IsCommentBangEnd()
    {
        if (IsActive)
        {
            var c = _content[_position];

            switch (c)
            {
                case '-':
                    Advance();
                    return IsCommentDashEnd();
                case '>':
                    _state = HtmlParseMode.PCData;
                    Advance();
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    #endregion

    #region Doctype

    private Token Doctype()
    {
        var start = _position;
        Advance(9);

        if (!IsActive)
        {
            return NewDoctype(start, _position);
        }

        var c = _content[_position];

        while (IsSpaceCharacter(c))
        {
            Advance();

            if (!IsActive)
            {
                return NewDoctype(start, _position);
            }

            c = _content[_position];
        }

        if (c == '>')
        {
            _state = HtmlParseMode.PCData;
            Advance();
        }
        else
        {
            ConsumeDoctypeName();
        }

        return NewDoctype(start, _position);
    }

    private void ConsumeDoctypeName()
    {
        while (IsActive)
        {
            var c = _content[_position];

            if (IsSpaceCharacter(c))
            {
                ConsumeDoctypeNameAfter();
                break;
            }
            else if (c == '>')
            {
                _state = HtmlParseMode.PCData;
                Advance();
                break;
            }

            _position++;
        }
    }

    private void ConsumeDoctypeNameAfter()
    {
        var c = SkipSpaces();

        if (IsActive)
        {
            if (c == '>')
            {
                Advance();
                _state = HtmlParseMode.PCData;
            }
            else if (ContinuesWithInsensitive("PUBLIC"))
            {
                Advance(6);
                ConsumeDoctypePublic();
            }
            else if (ContinuesWithInsensitive("SYSTEM"))
            {
                Advance(6);
                ConsumeDoctypeSystem();
            }
            else
            {
                ConsumeBogusDoctype();
            }
        }
    }

    private void ConsumeDoctypePublic()
    {
        if (IsActive)
        {
            var c = _content[_position++];

            if (IsSpaceCharacter(c))
            {
                ConsumeDoctypePublicIdentifierBefore();
            }
            else if (c == '"')
            {
                ConsumeDoctypePublicIdentifierDoubleQuoted();
            }
            else if (c == '\'')
            {
                ConsumeDoctypePublicIdentifierSingleQuoted();
            }
            else if (c == '>')
            {
                _state = HtmlParseMode.PCData;
            }
            else
            {
                ConsumeBogusDoctype();
            }
        }
    }

    private void ConsumeDoctypePublicIdentifierBefore()
    {
        var c = SkipSpaces();

        if (IsActive)
        {
            if (c == '"')
            {
                Advance();
                ConsumeDoctypePublicIdentifierDoubleQuoted();
            }
            else if (c == '\'')
            {
                Advance();
                ConsumeDoctypePublicIdentifierSingleQuoted();
            }
            else if (c == '>')
            {
                _state = HtmlParseMode.PCData;
                Advance();
            }
            else
            {
                ConsumeBogusDoctype();
            }
        }
    }

    private void ConsumeDoctypePublicIdentifierDoubleQuoted()
    {
        while (IsActive)
        {
            var c = _content[_position++];

            if (c == '"')
            {
                ConsumeDoctypePublicIdentifierAfter();
                return;
            }
            else if (c == '>')
            {
                _state = HtmlParseMode.PCData;
                return;
            }
        }
    }

    private void ConsumeDoctypePublicIdentifierSingleQuoted()
    {
        while (IsActive)
        {
            var c = _content[_position++];

            if (c == '"')
            {
                ConsumeDoctypePublicIdentifierAfter();
                return;
            }
            else if (c == '>')
            {
                _state = HtmlParseMode.PCData;
                return;
            }
        }
    }

    private void ConsumeDoctypePublicIdentifierAfter()
    {
        var c = GetNext();

        if (IsActive)
        {
            if (IsSpaceCharacter(c))
            {
                ConsumeDoctypeBetween();
            }
            else if (c == '>')
            {
                _state = HtmlParseMode.PCData;
                Advance();
            }
            else if (c == '"')
            {
                Advance();
                ConsumeDoctypeSystemIdentifierDoubleQuoted();
            }
            else if (c == '\'')
            {
                Advance();
                ConsumeDoctypeSystemIdentifierSingleQuoted();
            }
            else
            {
                ConsumeBogusDoctype();
            }
        }
    }

    private void ConsumeDoctypeBetween()
    {
        var c = SkipSpaces();

        if (IsActive)
        {
            if (c == '>')
            {
                _state = HtmlParseMode.PCData;
                Advance();
            }
            else if (c == '"')
            {
                Advance();
                ConsumeDoctypeSystemIdentifierDoubleQuoted();
            }
            else if (c == '\'')
            {
                Advance();
                ConsumeDoctypeSystemIdentifierSingleQuoted();
            }
            else
            {
                ConsumeBogusDoctype();
            }
        }
    }

    private void ConsumeDoctypeSystem()
    {
        var c = GetNext();

        if (IsSpaceCharacter(c))
        {
            _state = HtmlParseMode.PCData;
            ConsumeDoctypeSystemIdentifierBefore();
        }
        else if (c == '"')
        {
            ConsumeDoctypeSystemIdentifierDoubleQuoted();
        }
        else if (c == '\'')
        {
            ConsumeDoctypeSystemIdentifierSingleQuoted();
        }
        else if (c == '>')
        {
            Advance();
        }
        else if (IsActive)
        {
            ConsumeBogusDoctype();
        }
    }

    private void ConsumeDoctypeSystemIdentifierBefore()
    {
        var c = SkipSpaces();

        if (IsActive)
        {
            if (c == '"')
            {
                ConsumeDoctypeSystemIdentifierDoubleQuoted();
            }
            else if (c == '\'')
            {
                ConsumeDoctypeSystemIdentifierSingleQuoted();
            }
            else if (c == '>')
            {
                _state = HtmlParseMode.PCData;
                Advance();
            }
            else
            {
                ConsumeBogusDoctype();
            }
        }
    }

    private void ConsumeDoctypeSystemIdentifierDoubleQuoted()
    {
        while (IsActive)
        {
            var c = _content[_position++];

            if (c == '"')
            {
                ConsumeDoctypeSystemIdentifierAfter();
                return;
            }
            else if (c == '>')
            {
                _state = HtmlParseMode.PCData;
                return;
            }
        }
    }

    private void ConsumeDoctypeSystemIdentifierSingleQuoted()
    {
        while (IsActive)
        {
            var c = _content[_position++];

            switch (c)
            {
                case '\'':
                    ConsumeDoctypeSystemIdentifierAfter();
                    return;
                case '>':
                    _state = HtmlParseMode.PCData;
                    return;
            }
        }
    }

    private void ConsumeDoctypeSystemIdentifierAfter()
    {
        var c = SkipSpaces();

        if (c == '>')
        {
            _state = HtmlParseMode.PCData;
        }
        else if (IsActive)
        {
            ConsumeBogusDoctype();
        }
    }

    private void ConsumeBogusDoctype()
    {
        while (IsActive)
        {
            var c = _content[_position++];

            if (c == '>')
            {
                _state = HtmlParseMode.PCData;
                break;
            }
        }
    }

    #endregion

    #region Attributes

    private enum AttributeState : byte
    {
        BeforeName,
        Name,
        AfterName,
        BeforeValue,
        QuotedValue,
        AfterValue,
        UnquotedValue,
        SelfClose
    }

    private void ConsumeAttributes()
    {
        var state = AttributeState.BeforeName;
        var quote = '"';
        var c = '\0';

        while (IsActive)
        {
            switch (state)
            {
                case AttributeState.BeforeName:
                    {
                        c = SkipSpaces();

                        if (c == '/')
                        {
                            state = AttributeState.SelfClose;
                        }
                        else if (c == '>')
                        {
                            _state = HtmlParseMode.PCData;
                            return;
                        }
                        else if (c == '\'' || c == '"' || c == '=' || c == '<')
                        {
                            state = AttributeState.Name;
                        }
                        else if (IsActive)
                        {
                            state = AttributeState.Name;
                        }
                        else
                        {
                            return;
                        }

                        break;
                    }

                case AttributeState.Name:
                    {
                        c = GetNext();

                        if (c == '=')
                        {
                            state = AttributeState.BeforeValue;
                        }
                        else if (c == '>')
                        {
                            _state = HtmlParseMode.PCData;
                            return;
                        }
                        else if (IsSpaceCharacter(c))
                        {
                            state = AttributeState.AfterName;
                        }
                        else if (c == '/')
                        {
                            state = AttributeState.SelfClose;
                        }
                        else if (!IsActive)
                        {
                            return;
                        }

                        break;
                    }

                case AttributeState.AfterName:
                    {
                        c = SkipSpaces();

                        if (c == '>')
                        {
                            _state = HtmlParseMode.PCData;
                            Advance();
                            return;
                        }
                        else if (c == '=')
                        {
                            state = AttributeState.BeforeValue;
                        }
                        else if (c == '/')
                        {
                            state = AttributeState.SelfClose;
                        }
                        else if (IsActive)
                        {
                            state = AttributeState.Name;
                        }
                        else
                        {
                            return;
                        }

                        break;
                    }

                case AttributeState.BeforeValue:
                    {
                        c = SkipSpaces();

                        if (c == '"' || c == '\'')
                        {
                            state = AttributeState.QuotedValue;
                            quote = c;
                        }
                        else if (c == '&')
                        {
                            state = AttributeState.UnquotedValue;
                        }
                        else if (c == '>')
                        {
                            _state = HtmlParseMode.PCData;
                            Advance();
                            return;
                        }
                        else if (IsActive)
                        {
                            state = AttributeState.UnquotedValue;
                            c = GetNext();
                        }
                        else
                        {
                            return;
                        }

                        break;
                    }

                case AttributeState.QuotedValue:
                    {
                        c = GetNext();

                        if (c == quote)
                        {
                            state = AttributeState.AfterValue;
                        }
                        else if (!IsActive)
                        {
                            return;
                        }

                        break;
                    }

                case AttributeState.UnquotedValue:
                    {
                        if (c == '>')
                        {
                            _state = HtmlParseMode.PCData;
                            Advance();
                            return;
                        }
                        else if (IsSpaceCharacter(c))
                        {
                            state = AttributeState.BeforeName;
                        }
                        else if (IsActive)
                        {
                            c = GetNext();
                        }
                        else
                        {
                            return;
                        }

                        break;
                    }

                case AttributeState.AfterValue:
                    {
                        c = GetNext();

                        if (c == '>')
                        {
                            _state = HtmlParseMode.PCData;
                            Advance();
                            return;
                        }
                        else if (IsSpaceCharacter(c))
                        {
                            state = AttributeState.BeforeName;
                        }
                        else if (c == '/')
                        {
                            state = AttributeState.SelfClose;
                        }
                        else if (IsActive)
                        {
                            Back();
                            state = AttributeState.BeforeName;
                        }
                        else
                        {
                            return;
                        }

                        break;
                    }

                case AttributeState.SelfClose:
                    {
                        if (!IsTagSelfClosingInner())
                        {
                            state = AttributeState.BeforeName;
                            break;
                        }

                        return;
                    }
            }
        }
    }

    #endregion

    #region Script

    private enum ScriptState : byte
    {
        Normal,
        OpenTag,
        EndTag,
        StartEscape,
        Escaped,
        StartEscapeDash,
        EscapedDash,
        EscapedDashDash,
        EscapedOpenTag,
        EscapedEndTag,
        EscapedNameEndTag,
        StartDoubleEscape,
        EscapedDouble,
        EscapedDoubleDash,
        EscapedDoubleDashDash,
        EscapedDoubleOpenTag,
        EndDoubleEscape
    }

    private Token ScriptData()
    {
        const int scriptLength = 6;

        var c = _content[_position];
        var state = ScriptState.Normal;
        var start = _position;
        var marker = 0;

        while (IsActive)
        {
            switch (state)
            {
                case ScriptState.Normal:
                    {
                        if (c == '<')
                        {
                            state = ScriptState.OpenTag;
                            continue;
                        }

                        c = GetNext();
                        break;
                    }

                case ScriptState.OpenTag:
                    {
                        c = GetNext();

                        if (c == '/')
                        {
                            state = ScriptState.EndTag;
                        }
                        else if (c == '!')
                        {
                            state = ScriptState.StartEscape;
                        }
                        else
                        {
                            state = ScriptState.Normal;
                        }

                        break;
                    }

                case ScriptState.StartEscape:
                    {
                        c = GetNext();

                        if (c == '-')
                        {
                            state = ScriptState.StartEscapeDash;
                        }
                        else
                        {
                            state = ScriptState.Normal;
                        }

                        break;
                    }

                case ScriptState.StartEscapeDash:
                    {
                        c = GetNext();

                        if (c == '-')
                        {
                            state = ScriptState.EscapedDashDash;
                        }
                        else
                        {
                            state = ScriptState.Normal;
                        }

                        break;
                    }

                case ScriptState.EndTag:
                    {
                        c = GetNext();
                        var offset = _position;

                        while (IsLetter(c))
                        {
                            c = GetNext();
                            var isspace = IsSpaceCharacter(c);
                            var isclosed = c == '>';
                            var isslash = c == '/';
                            var hasLength = _position - offset == scriptLength;
                            var isend = isspace || isclosed || isslash;

                            if (hasLength && isend && _content.Slice(offset, _position).Equals("script", StringComparison.OrdinalIgnoreCase))
                            {
                                if (offset - start > 2)
                                {
                                    return NewCharacter(start, _position);
                                }

                                if (isspace)
                                {
                                    ConsumeAttributes();
                                    return NewTagOpen(start, _position);
                                }
                                else if (isslash)
                                {
                                    ConsumeTagSelfClosing();
                                    return NewTagClose(start, _position);
                                }
                                else if (isclosed)
                                {
                                    _state = HtmlParseMode.PCData;
                                    return NewTagClose(start, _position);
                                }
                            }
                        }

                        state = ScriptState.Normal;
                        break;
                    }

                case ScriptState.Escaped:
                    {
                        switch (c)
                        {
                            case '-':
                                c = GetNext();
                                state = ScriptState.EscapedDash;
                                continue;
                            case '<':
                                c = GetNext();
                                state = ScriptState.EscapedOpenTag;
                                continue;
                            default:
                                state = ScriptState.Normal;
                                continue;
                        }
                    }

                case ScriptState.EscapedDash:
                    {
                        switch (c)
                        {
                            case '-':
                                state = ScriptState.EscapedDashDash;
                                continue;
                            case '<':
                                c = GetNext();
                                state = ScriptState.EscapedOpenTag;
                                continue;
                            default:
                                break;
                        }

                        c = GetNext();
                        state = ScriptState.Escaped;
                        break;
                    }

                case ScriptState.EscapedDashDash:
                    {
                        c = GetNext();

                        switch (c)
                        {
                            case '-':
                                break;
                            case '<':
                                c = GetNext();
                                state = ScriptState.EscapedOpenTag;
                                continue;
                            case '>':
                                c = GetNext();
                                state = ScriptState.Normal;
                                continue;
                            default:
                                c = GetNext();
                                state = ScriptState.Escaped;
                                continue;
                        }

                        break;
                    }

                case ScriptState.EscapedOpenTag:
                    {
                        if (c == '/')
                        {
                            c = GetNext();
                            state = ScriptState.EscapedEndTag;
                        }
                        else if (IsLetter(c))
                        {
                            state = ScriptState.StartDoubleEscape;
                            marker = _position;
                        }
                        else
                        {
                            state = ScriptState.Escaped;
                        }

                        break;
                    }

                case ScriptState.EscapedEndTag:
                    {
                        if (IsLetter(c))
                        {
                            state = ScriptState.EscapedNameEndTag;
                            marker = _position;
                        }
                        else
                        {
                            state = ScriptState.Escaped;
                        }

                        break;
                    }

                case ScriptState.EscapedNameEndTag:
                    {
                        c = GetNext();
                        var hasLength = _position - marker == scriptLength;

                        if (hasLength && (c == '/' || c == '>' || IsSpaceCharacter(c)))
                        {
                            if (_content.Slice(marker, _position).Equals("script", StringComparison.OrdinalIgnoreCase))
                            {
                                Back(scriptLength + 3);
                                return NewCharacter(start, _position);
                            }
                        }
                        else if (!IsLetter(c))
                        {
                            state = ScriptState.Escaped;
                        }

                        break;
                    }

                case ScriptState.StartDoubleEscape:
                    {
                        c = GetNext();
                        var hasLength = _position - marker == scriptLength;

                        if (hasLength && (c == '/' || c == '>' || IsSpaceCharacter(c)))
                        {
                            var isscript = _content.Slice(marker, _position).Equals("script", StringComparison.OrdinalIgnoreCase);
                            c = GetNext();
                            state = isscript ? ScriptState.EscapedDouble : ScriptState.Escaped;
                        }
                        else if (!IsLetter(c))
                        {
                            state = ScriptState.Escaped;
                        }

                        break;
                    }

                case ScriptState.EscapedDouble:
                    {
                        switch (c)
                        {
                            case '-':
                                c = GetNext();
                                state = ScriptState.EscapedDoubleDash;
                                continue;

                            case '<':
                                c = GetNext();
                                state = ScriptState.EscapedDoubleOpenTag;
                                continue;
                        }

                        c = GetNext();
                        break;
                    }

                case ScriptState.EscapedDoubleDash:
                    {
                        switch (c)
                        {
                            case '-':
                                state = ScriptState.EscapedDoubleDashDash;
                                continue;

                            case '<':
                                c = GetNext();
                                state = ScriptState.EscapedDoubleOpenTag;
                                continue;
                        }

                        state = ScriptState.EscapedDouble;
                        break;
                    }

                case ScriptState.EscapedDoubleDashDash:
                    {
                        c = GetNext();

                        switch (c)
                        {
                            case '-':
                                break;

                            case '<':
                                c = GetNext();
                                state = ScriptState.EscapedDoubleOpenTag;
                                continue;

                            case '>':
                                c = GetNext();
                                state = ScriptState.Normal;
                                continue;

                            default:
                                c = GetNext();
                                state = ScriptState.EscapedDouble;
                                continue;
                        }

                        break;
                    }

                case ScriptState.EscapedDoubleOpenTag:
                    {
                        if (c == '/')
                        {
                            state = ScriptState.EndDoubleEscape;
                        }
                        else
                        {
                            state = ScriptState.EscapedDouble;
                        }

                        break;
                    }

                case ScriptState.EndDoubleEscape:
                    {
                        c = GetNext();
                        var hasLength = _position - marker == scriptLength;

                        if (hasLength && (IsSpaceCharacter(c) || c == '/' || c == '>'))
                        {
                            var isscript = _content.Slice(marker, _position).Equals("script", StringComparison.OrdinalIgnoreCase);
                            c = GetNext();
                            state = isscript ? ScriptState.Escaped : ScriptState.EscapedDouble;
                        }
                        else if (!IsLetter(c))
                        {
                            state = ScriptState.EscapedDouble;
                        }

                        break;
                    }
            }
        }

        return NewCharacter(start, _position);
    }

    #endregion

    #region Tokens

    private readonly Token NewCharacter(int start, int end)
    {
        return new Token(TokenType.Character, _content[start..end]);
    }

    private readonly Token NewProcessingInstruction(int start, int end)
    {
        return new Token(TokenType.ProcessingInstruction, _content[start..end]);
    }

    private readonly Token NewComment(int start, int end)
    {
        return new Token(TokenType.Comment, _content[start..end]);
    }

    private readonly Token NewDoctype(int start, int end)
    {
        return new Token(TokenType.Doctype, _content[start..end]);
    }

    private readonly Token NewTagOpen(int start, int end)
    {
        return new Token(TokenType.StartTag, _content[start..end]);
    }

    private readonly Token NewTagClose(int start, int end)
    {
        return new Token(TokenType.EndTag, _content[start..end]);
    }

    #endregion

    #region Helpers

    enum HtmlParseMode : byte
    {
        PCData,
        RCData,
        Plaintext,
        Rawtext,
        Script
    }

    /// <summary>
    /// Determines if the given character is a uppercase character (A-Z) as
    /// specified here:
    /// http://www.whatwg.org/specs/web-apps/current-work/multipage/common-microsyntaxes.html#uppercase-ascii-letters
    /// </summary>
    /// <param name="c">The character to examine.</param>
    /// <returns>The result of the test.</returns>
    private static bool IsUppercaseAscii(char c) => c >= 0x41 && c <= 0x5a;

    /// <summary>
    /// Determines if the given character is a lowercase character (a-z) as
    /// specified here:
    /// http://www.whatwg.org/specs/web-apps/current-work/multipage/common-microsyntaxes.html#lowercase-ascii-letters
    /// </summary>
    /// <param name="c">The character to examine.</param>
    /// <returns>The result of the test.</returns>
    private static bool IsLowercaseAscii(char c) => c >= 0x61 && c <= 0x7a;

    /// <summary>
    /// Determines if the given character is a space character as specified
    /// here:
    /// http://www.whatwg.org/specs/web-apps/current-work/multipage/common-microsyntaxes.html#space-character
    /// </summary>
    /// <param name="c">The character to examine.</param>
    /// <returns>The result of the test.</returns>
    private static bool IsSpaceCharacter(char c) => c == 0x20 || c == 0x09 || c == 0x0a || c == 0x0d || c == 0x0c;

    /// <summary>
    /// Gets if the character is actually a (A-Z,a-z) letter.
    /// </summary>
    /// <param name="c">The character to examine.</param>
    /// <returns>The result of the test.</returns>
    private static bool IsLetter(char c) => IsUppercaseAscii(c) || IsLowercaseAscii(c);

    private char GetNext()
    {
        Advance();

        if (IsActive)
        {
            return _content[_position];
        }
        
        return char.MaxValue;
    }

    private void Back(int n = 1)
    {
        _position -= n;

        if (_position < 0)
        {
            _position = 0;
        }
    }

    private void Advance(int n = 1)
    {
        _position += n;

        if (!IsActive)
        {
            _position = _content.Length;
        }
    }

    /// <summary>
    /// Checks if the source continues with the given string.
    /// The comparison is case-insensitive.
    /// </summary>
    /// <param name="s">The string to compare to.</param>
    /// <returns>True if the source continues with the given string.</returns>
    private bool ContinuesWithInsensitive(string s)
    {
        var end = _position + s.Length;

        if (end <= _content.Length)
        {
            var content = _content[_position..end];
            return content.Equals(s.AsSpan(), StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Checks if the source continues with the given string.
    /// The comparison is case-sensitive.
    /// </summary>
    /// <param name="s">The string to compare to.</param>
    /// <returns>True if the source continues with the given string.</returns>
    private bool ContinuesWithSensitive(string s)
    {
        var end = _position + s.Length;

        if (end <= _content.Length)
        {
            var content = _content[_position..end];
            return content.Equals(s.AsSpan(), StringComparison.Ordinal);
        }

        return false;
    }

    private char SkipSpaces()
    {
        var c = GetNext();

        while (IsSpaceCharacter(c))
        {
            c = GetNext();
        }

        return c;
    }

    #endregion
}
