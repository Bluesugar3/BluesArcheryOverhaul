"""Set one entry in a MelonPreferences .cfg file, keeping its line endings and everything else.

    python tools/set-cfg.py <file> <section> <key> <value as written in the file, e.g. "Debug" or true>

Adds the section or the key when missing (the mod reads a value that is already in the file when it first creates the
entry). The section is found by its header on its own line: other entries' descriptions mention section names too.
Only run it while the game that owns the file is closed, or for settings the mod re-reads live.
"""
import io
import re
import sys


def put(text, section, key, value):
    nl = '\r\n' if '\r\n' in text else '\n'
    line = key + ' = ' + value
    m = re.search(r'(?m)^\[' + re.escape(section) + r'\]\r?$', text)
    if not m:
        if text and not text.endswith(nl):
            text += nl
        return text + nl + '[' + section + ']' + nl + line + nl
    end = text.find('\n[', m.end())
    if end < 0:
        end = len(text)
    sec = text[m.start():end]
    if re.search(r'(?m)^' + re.escape(key) + r' = ', sec):
        sec = re.sub(r'(?m)^' + re.escape(key) + r' = .*?(\r?)$', lambda x: line + x.group(1), sec, count=1)
    else:
        head = sec.split(nl, 1)
        sec = head[0] + nl + line + nl + (head[1] if len(head) > 1 else '')
    return text[:m.start()] + sec + text[end:]


def main():
    if len(sys.argv) != 5:
        sys.exit(__doc__)
    path, section, key, value = sys.argv[1:5]
    text = io.open(path, encoding='utf-8', newline='').read()
    new = put(text, section, key, value)
    if new != text:
        io.open(path, 'w', encoding='utf-8', newline='').write(new)
    print('[' + section + '] ' + key + ' = ' + value + ('' if new != text else ' (already)'))


if __name__ == '__main__':
    # self-check: adding a section, adding a key, replacing a value, CRLF kept
    t = put('[A]\r\nX = 1\r\n', 'B', 'K', 'true')
    assert t == '[A]\r\nX = 1\r\n\r\n[B]\r\nK = true\r\n', repr(t)
    assert put(t, 'B', 'K', 'false').endswith('[B]\r\nK = false\r\n')
    assert put('[A]\nX = 1\n[B]\nY = 2\n', 'A', 'Z', '"q"') == '[A]\nZ = "q"\nX = 1\n[B]\nY = 2\n'
    main()
