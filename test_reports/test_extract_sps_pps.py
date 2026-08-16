"""
Line-by-line Python port of C# ExtractSpsPps() from
/app/windows/SecondScreen.Core/Video/VideoStreamer.cs (lines 140-171).

Test vectors:
  (a) 4-byte startcodes: SPS(0x67)+PPS(0x68)+IDR(0x65) => returns SPS+PPS with startcodes
  (b) IDR only => None
  (c) mixed 3-byte and 4-byte startcodes => still works
  (d) SPS present but no PPS => None
  (e) prepend-to-keyframe path from OnEncodedFrame

Adversarial edge cases (must NOT raise IndexError):
  - empty buffer
  - <4-byte buffer
  - startcode at very end (no NAL body)
  - only zero bytes
  - alternating false-positive prefixes
"""

def extract_sps_pps(d: bytes):
    """Port of C# private static byte[]? ExtractSpsPps(byte[] d).
    Loop: while (i + 3 < d.Length). sc3: d[i..i+2]==00 00 01. sc4: d[i..i+3]==00 00 00 01.
    Take(end) captures the NAL running from `start` to `end` if start>=0. type is d[i+sc] & 0x1F.
    After the loop, Take(d.Length) closes the last NAL.
    Returns None unless BOTH SPS (type==7) and PPS (type==8) were found; then concatenates them.
    """
    sps = None
    pps = None
    start = -1
    type_ = 0

    def take(end):
        nonlocal sps, pps
        if start < 0:
            return
        if type_ == 7:
            sps_slice = bytes(d[start:end])
            # emulate `sps = d.AsSpan(start, end - start).ToArray();`
            # (assigning outer via closure below)
            outer['sps'] = sps_slice
        elif type_ == 8:
            outer['pps'] = bytes(d[start:end])

    outer = {'sps': None, 'pps': None}
    i = 0
    n = len(d)
    while i + 3 < n:
        sc3 = d[i] == 0 and d[i + 1] == 0 and d[i + 2] == 1
        sc4 = d[i] == 0 and d[i + 1] == 0 and d[i + 2] == 0 and d[i + 3] == 1
        if sc3 or sc4:
            take(i)
            sc = 3 if sc3 else 4
            if i + sc >= n:
                start = -1
                break
            start = i
            type_ = d[i + sc] & 0x1F
            i += sc + 1
        else:
            i += 1
    take(n)
    sps, pps = outer['sps'], outer['pps']
    if sps is None or pps is None:
        return None
    return sps + pps


def prepend_headers_on_keyframe(cached_headers, frame, is_keyframe):
    """Port of OnEncodedFrame prepend path (lines 117-125): if frame is keyframe but doesn't
    itself contain SPS/PPS, prepend the last-known cached SPS+PPS Annex-B bytes."""
    hdrs = extract_sps_pps(frame)
    if hdrs is not None:
        cached_headers = hdrs
        return frame, cached_headers  # frame already has headers; still update cache
    if is_keyframe and cached_headers is not None:
        return cached_headers + frame, cached_headers
    return frame, cached_headers


# --- Test vectors -----------------------------------------------------------------------

def sc4(): return b"\x00\x00\x00\x01"
def sc3(): return b"\x00\x00\x01"


def test_a_full_frame_4byte_startcodes():
    sps_body = b"\x67\x42\x00\x1F\xAB\xCD"   # NAL type 7 (SPS)
    pps_body = b"\x68\xCE\x38\x80"           # NAL type 8 (PPS)
    idr_body = b"\x65\x88\x80\x10\x00\x00\x03\x00" + b"\xAA" * 32  # NAL type 5 (IDR)
    frame = sc4() + sps_body + sc4() + pps_body + sc4() + idr_body
    out = extract_sps_pps(frame)
    expected = sc4() + sps_body + sc4() + pps_body
    assert out == expected, f"(a) SPS+PPS extraction mismatch\ngot     : {out.hex() if out else None}\nexpected: {expected.hex()}"


def test_b_idr_only_returns_none():
    frame = sc4() + b"\x65\x88\x80\x10\x00\xFF" * 4
    out = extract_sps_pps(frame)
    assert out is None, f"(b) IDR-only must return None, got {out}"


def test_c_mixed_3byte_and_4byte_startcodes():
    sps_body = b"\x67\x42\x00\x1F"
    pps_body = b"\x68\xCE\x38\x80"
    idr_body = b"\x65\xAB\xCD\xEF"
    # SPS with 3-byte, PPS with 4-byte, IDR with 3-byte
    frame = sc3() + sps_body + sc4() + pps_body + sc3() + idr_body
    out = extract_sps_pps(frame)
    expected = sc3() + sps_body + sc4() + pps_body
    assert out == expected, f"(c) mixed startcode mismatch\ngot     : {out.hex() if out else None}\nexpected: {expected.hex()}"


def test_d_sps_without_pps_returns_none():
    frame = sc4() + b"\x67\x42\x00\x1F\xAB" + sc4() + b"\x65\xDE\xAD\xBE\xEF"  # SPS + IDR, no PPS
    out = extract_sps_pps(frame)
    assert out is None, f"(d) SPS-without-PPS must return None, got {out}"


def test_e_prepend_path_recovers_missing_headers_before_idr():
    # First frame contains SPS+PPS+IDR — cache should learn headers.
    sps_body = b"\x67\x42\x00\x1F"
    pps_body = b"\x68\xCE\x38\x80"
    idr = sc4() + b"\x65\xAB\xCD"
    first = sc4() + sps_body + sc4() + pps_body + idr
    _, cache = prepend_headers_on_keyframe(None, first, True)
    assert cache == sc4() + sps_body + sc4() + pps_body

    # Later keyframe WITHOUT SPS/PPS => must be prepended with cached headers.
    lonely_idr = sc4() + b"\x65\x11\x22\x33\x44"
    patched, _ = prepend_headers_on_keyframe(cache, lonely_idr, True)
    assert patched.startswith(cache), "(e) cached SPS+PPS must be prepended to bare IDR"
    assert patched.endswith(lonely_idr), "(e) IDR bytes must follow the prepended headers"
    # Decoder now sees SPS, PPS, IDR in order.
    parsed_after = extract_sps_pps(patched[:len(cache) + 4])  # SPS+PPS visible immediately
    assert parsed_after is None or parsed_after == cache  # SPS+PPS present but IDR incomplete slice — OK


# --- Adversarial bounds tests (must NOT throw) ------------------------------------------

def test_empty_buffer():
    assert extract_sps_pps(b"") is None


def test_tiny_buffers_no_index_error():
    for buf in [b"\x00", b"\x00\x00", b"\x00\x00\x01", b"\x00\x00\x00\x01"]:
        # None of these produce a full SPS+PPS pair; must return None cleanly.
        assert extract_sps_pps(buf) is None


def test_startcode_at_very_end():
    # 4-byte startcode literally at the tail with no NAL body: `i + sc >= d.Length` breaks safely.
    buf = b"\xFF\xFF\x00\x00\x00\x01"  # ends with startcode
    assert extract_sps_pps(buf) is None
    # 3-byte startcode at very end
    buf2 = b"\xFF\xFF\xFF\x00\x00\x01"
    assert extract_sps_pps(buf2) is None


def test_only_zeros():
    assert extract_sps_pps(b"\x00" * 32) is None


def test_startcode_immediately_followed_by_eof_after_nal_type():
    # `[00 00 00 01 0x67]` — startcode + SPS nal type byte, no body. Must not throw.
    # Note: at i=0, sc4 true, i+sc=4, len=5, so 4>=5 is false => start=0, type=7, i=5.
    # Loop condition: 5+3<5 => false, exit. Take(5) => sps = full buffer (5 bytes).
    # But pps is None, so returns None.
    buf = b"\x00\x00\x00\x01\x67"
    assert extract_sps_pps(buf) is None


def test_no_index_error_on_random_bytes():
    import random
    random.seed(0xC0FFEE)
    for _ in range(200):
        n = random.randint(0, 64)
        buf = bytes(random.randint(0, 255) for _ in range(n))
        # Just ensure it doesn't raise.
        extract_sps_pps(buf)


if __name__ == "__main__":
    import traceback
    tests = [
        test_a_full_frame_4byte_startcodes,
        test_b_idr_only_returns_none,
        test_c_mixed_3byte_and_4byte_startcodes,
        test_d_sps_without_pps_returns_none,
        test_e_prepend_path_recovers_missing_headers_before_idr,
        test_empty_buffer,
        test_tiny_buffers_no_index_error,
        test_startcode_at_very_end,
        test_only_zeros,
        test_startcode_immediately_followed_by_eof_after_nal_type,
        test_no_index_error_on_random_bytes,
    ]
    passed = failed = 0
    for t in tests:
        try:
            t()
            print(f"PASS  {t.__name__}")
            passed += 1
        except Exception as e:
            print(f"FAIL  {t.__name__}: {e}")
            traceback.print_exc()
            failed += 1
    print(f"\n{passed}/{passed+failed} passed")
    raise SystemExit(0 if failed == 0 else 1)
