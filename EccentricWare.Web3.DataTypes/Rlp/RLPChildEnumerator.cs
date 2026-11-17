using EccentricWare.Web3.DataTypes.Utils;

namespace EccentricWare.Web3.DataTypes.Rlp;

/// <summary>
/// Sequential, zero-copy enumerator over the direct children of a validated list, obtained from
/// <see cref="RLPData.TryGetChildren"/>. Each <see cref="MoveNext"/> reads one child header (constrained to the list
/// end) and advances by the whole child; descendants are not re-scanned because they were validated at root entry.
/// Children keep the document-relative coordinate base of the original document.
/// </summary>
/// <remarks>
/// <see cref="Current"/> is defined only after <see cref="MoveNext"/> returned <c>true</c> and before the next
/// <see cref="MoveNext"/> returns <c>false</c>; anywhere else (before the first call, after the end, on
/// <c>default</c>) it throws <see cref="InvalidOperationException"/>, a programming error rather than an input error.
/// <see cref="MoveNext"/> returns <c>false</c> only at the exact list end. If the document bytes were modified after
/// validation so that a child header no longer fits its list, it throws <see cref="InvalidOperationException"/>
/// instead of treating the damage as the end of the list or reading outside the document. The document must stay
/// stable while the enumerator is in use.
/// </remarks>
public ref struct RLPChildEnumerator
{
    private readonly ReadOnlySpan<byte> _document;
    private readonly int _listEnd;
    private int _cursor;
    private int _currentStart;
    private int _currentPayloadStart;
    private RLPKind _currentKind;

    internal RLPChildEnumerator(ReadOnlySpan<byte> document, int payloadStart, int listEnd)
    {
        _document = document;
        _cursor = payloadStart;
        _listEnd = listEnd;
        _currentKind = RLPKind.None;
    }

    /// <summary>The current child. Throws <see cref="InvalidOperationException"/> outside a valid position.</summary>
    public readonly RLPData Current
    {
        get
        {
            if (_currentKind == RLPKind.None)
            {
                ThrowHelper.ThrowInvalidOperationExceptionRlpEnumeratorNoCurrent();
            }

            // _cursor is the end of the current child after a successful MoveNext.
            return new RLPData(_document, _currentKind, _currentStart, _currentPayloadStart, _cursor);
        }
    }

    /// <summary>
    /// Advances to the next direct child. Returns <c>false</c> once the exact end of the list is reached.
    /// </summary>
    public bool MoveNext()
    {
        int start = _cursor;
        if (start == _listEnd)
        {
            _currentKind = RLPKind.None;
            return false;
        }

        if (RLPHeaderReader.TryRead(_document, start, _listEnd, out RLPHeader child) != RLPHeaderStatus.Valid)
        {
            _currentKind = RLPKind.None;
            ThrowHelper.ThrowInvalidOperationExceptionRlpDocumentModified();
        }

        _currentStart = start;
        _currentPayloadStart = child.PayloadStart;
        _currentKind = child.Kind;
        _cursor = child.ItemEnd;
        return true;
    }
}
