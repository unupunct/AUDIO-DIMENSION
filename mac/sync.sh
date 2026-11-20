#!/bin/bash
# Copies the Mac sources to the build Mac (dev helper). Usage: mac/sync.sh user@host
set -e
cd "$(dirname "$0")/.."
tar czf - --exclude=mac/build LICENSE THIRD_PARTY_NOTICES.md mac | ssh -o BatchMode=yes "$1" "mkdir -p ~/work/AudioDimension && cd ~/work/AudioDimension && tar xzf -"
