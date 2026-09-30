#!/usr/bin/env bash
# Rebuilds safepatch-perf-experiment: every fix and perf branch merged onto 0.54.4, then the generated code
# regenerated from the merged generator (minus the generator's drift at 0.54.4).
set -u
PR=/c/Users/matth/source/repos/Mutagen-wt/pr; GEN=/c/Users/matth/source/repos/Mutagen-wt/gen
cd $PR; git reset -q --hard; git checkout -q -B safepatch-perf-experiment 0188012
for b in fix/unsigned-two-byte-enums fix/enum-parsevalue-result fix/nullable-enum-fallback-parse fix/overlay-declared-defaults fix/cumulative-break-flags fix/gendered-item-equality perf/cache-overlay-groups perf/deferred-overlay-fill perf/record-batches; do
  git merge -q --no-ff --no-edit $b > /dev/null 2>&1
  git diff --name-only --diff-filter=U | grep _Generated | while read f; do git checkout -q --ours -- "$f"; git add "$f"; done
  for f in $(git diff --name-only --diff-filter=U); do
    case $f in
      *EnumBinaryTranslation.cs) git checkout -q --theirs -- $f
        sed -i 's/            2 => reader.ReadInt16(),/            2 => reader.ReadUInt16(),/; s/                val = BinaryPrimitives.ReadInt16LittleEndian(data);/                val = BinaryPrimitives.ReadUInt16LittleEndian(data);/' $f; git add $f;;
      *BinaryTranslationGeneration.cs) git checkout -q --ours -- $f
        python - "$(cygpath -w $f)" <<'EOF'
import sys
p=sys.argv[1]; d=open(p,'rb').read().decode('utf-8-sig')
a='    public string NamespacePrefix => NeedsNamespacePrefix ? Namespace : string.Empty;\r\n'
b=a+'''
    /// <summary>
    /// The break flags to set when data stops at break <paramref name="first"/>: that break and every later one, since
    /// the fields after each are all absent. The overlay sets them this way, testing each break against the length.
    /// </summary>
    public static string BreaksFrom(string enumName, int first, int count) =>
        string.Join(" | ", Enumerable.Range(first, Math.Max(1, count - first)).Select(i => $"{enumName}.Break{i}"));
'''.replace('\n','\r\n')
assert d.count(a)==1; d=d.replace(a,b)
open(p,'wb').write(d.encode('utf-8'))
EOF
        git -c core.autocrlf=false add $f;;
      *) echo "UNEXPECTED CONFLICT $f"; exit 1;;
    esac
  done
  git -c core.autocrlf=false commit -q --no-edit > /dev/null 2>&1
done
cd $GEN; git reset -q --hard; git checkout -q -B tmp/combined gen-drift && git checkout safepatch-perf-experiment -- Mutagen.Bethesda.Generation
dotnet build Mutagen.Bethesda.Generator.All -c Release -p:DisableGitVersionTask=true -p:GeneratePackageOnBuild=false 2>&1 | grep -E " error |rror\(s\)" | head -3
(cd Mutagen.Bethesda.Generator.All/bin/Release/net10.0 && ./Mutagen.Bethesda.Generator.All.exe > ../../../../../gen-fix.log 2>&1)
git add -A && git -c core.safecrlf=false commit -q -m "tmp: combined"
python - <<'EOF'
import subprocess, os, tempfile
gen=r'C:\Users\matth\source\repos\Mutagen-wt\gen'; pr=r'C:\Users\matth\source\repos\Mutagen-wt\pr'
def git(*a): return subprocess.run(['git',*a],cwd=gen,capture_output=True)
files=[f for f in git('diff','--name-only','gen-drift','tmp/combined').stdout.decode().splitlines() if f.endswith('_Generated.cs')]
def blob(ref,f):
    r=git('show',f'{ref}:{f}'); return r.stdout if r.returncode==0 else None
tmp=tempfile.mkdtemp(); conflicts=0
for f in files:
    base0=blob('0188012',f); drift=blob('gen-drift',f); comb=blob('tmp/combined',f)
    if base0 is None or drift is None or base0==drift: out=comb
    else:
        paths=[]
        for n,d in (('ours',base0),('base',drift),('theirs',comb)):
            p=os.path.join(tmp,n); open(p,'wb').write(d); paths.append(p)
        r=subprocess.run(['git','merge-file','-p',*paths],capture_output=True); out=r.stdout
        if r.returncode!=0: conflicts+=1; print('CONFLICT',f)
    open(os.path.join(pr,f),'wb').write(out)
print(len(files),'generated files,',conflicts,'conflicts')
EOF
cd $PR; git -c core.autocrlf=false add -A -- '*_Generated.cs'
git status --short | grep -v '^??'
git -c core.autocrlf=false commit -q -m "Regenerate for the merged generator changes

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" && git log --oneline -1
