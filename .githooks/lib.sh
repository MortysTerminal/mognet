#!/bin/bash
# Shared logic for the commit guards. Sourced, not executed.
# Contains no private values: the deny list lives outside all repos.

ALLOWED_NAME="MortysTerminal"
ALLOWED_EMAIL="111351643+MortysTerminal@users.noreply.github.com"
DENYFILE_DISPLAY="~/.config/mognet/private-denylist.txt"

# Forbidden AI attribution (extended regex, use with grep -iE).
ATTR_RE='co-authored-by|generated with|claude code|noreply@anthropic\.com|claude\.ai/code|(generated|written|created|authored|made|assisted|developed)[^.]{0,30}[[:space:]](by|with)[[:space:]]+(an?[[:space:]]+)?(ai|claude|anthropic)([^[:alnum:]]|$)'

# Prepare the private term list (deny list file + current user name).
# Fails closed when the file is missing. Sets DENY_TERMS (temp file; caller removes it).
deny_init() {
  DENYFILE="$HOME/.config/mognet/private-denylist.txt"
  if [ ! -r "$DENYFILE" ]; then
    echo "BLOCKED: private deny list not found. Create $DENYFILE (one term per line, '#' for comments)." >&2
    echo "         See docs/runbooks/commit-guards.md. Guards fail closed without it." >&2
    return 1
  fi
  DENY_TERMS=$(mktemp "${TMPDIR:-/tmp}/mognet-deny.XXXXXX") || return 1
  { tr -d '\r' < "$DENYFILE" | grep -v -E '^[[:space:]]*(#|$)'; id -un; } | grep -v '^$' > "$DENY_TERMS"
}

# deny_scan <label>: reads text on stdin; reports line numbers only, never the term.
deny_scan() {
  local hits
  hits=$(grep -n -i -F -f "$DENY_TERMS" | cut -d: -f1 | head -20 | tr '\n' ' ')
  [ -z "$hits" ] && return 0
  echo "BLOCKED: private term found in $1 (line(s): $hits). The term is not shown on purpose." >&2
  return 1
}

# ident_email "Name <mail> 123 +0000" -> mail
ident_email() { sed -E 's/^.*<(.*)>.*$/\1/'; }
