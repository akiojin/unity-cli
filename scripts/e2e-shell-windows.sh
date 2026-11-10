#!/usr/bin/env bash
# Native jq must preserve Unity hierarchy paths and emit LF inside Git Bash.
case "${OSTYPE}" in
  msys*|cygwin*)
    jq() {
      local converted=()
      local argument
      while [[ $# -gt 0 ]]; do
        case "$1" in
          --arg|--argjson)
            # Unity hierarchy paths are literal values, including /Canvas/....
            converted+=("$1" "$2" "$3")
            shift 3
            ;;
          *)
            argument="$1"
            # File/module inputs still need native paths when conversion is off.
            if [[ "${argument}" == /* && -e "${argument}" ]]; then
              argument="$(cygpath -m "${argument}")"
            fi
            converted+=("${argument}")
            shift
            ;;
        esac
      done
      MSYS2_ARG_CONV_EXCL='*' command jq --binary "${converted[@]}"
    }
    export -f jq
    ;;
esac
