#!/usr/bin/env bash

set -euo pipefail

: "${RELEASE_TAG:?RELEASE_TAG is required}"
: "${BUILD_CONFIGURATION:?BUILD_CONFIGURATION is required}"
: "${GH_TOKEN:?GH_TOKEN is required}"
: "${MODELSCOPE_TOKEN:?MODELSCOPE_TOKEN is required}"

MODELSCOPE_DATASET="${MODELSCOPE_DATASET:-AnxunBCX/PCL_Nex}"
MODELSCOPE_BRANCH="${MODELSCOPE_BRANCH:-master}"

if [[ ! "$RELEASE_TAG" =~ ^v[1-9][0-9]{3}\.(0[1-9]|1[0-2])\.(0|[1-9][0-9]*)$ ]]; then
  echo "Invalid release tag: $RELEASE_TAG" >&2
  exit 1
fi

if [[ "$BUILD_CONFIGURATION" != "Release" && "$BUILD_CONFIGURATION" != "Beta" ]]; then
  echo "Invalid build configuration: $BUILD_CONFIGURATION" >&2
  exit 1
fi

release_assets_dir="${RUNNER_TEMP:?RUNNER_TEMP is required}/release-assets"
mkdir -p "$release_assets_dir"

gh release download "$RELEASE_TAG" \
  --repo "$GITHUB_REPOSITORY" \
  --pattern "PCL2_Nex_${BUILD_CONFIGURATION}_*" \
  --dir "$release_assets_dir" \
  --clobber
gh release view "$RELEASE_TAG" \
  --repo "$GITHUB_REPOSITORY" \
  --json body \
  --jq '.body' > "$release_assets_dir/RELEASE_NOTE.md"

mapfile -t release_assets < <(
  find "$release_assets_dir" -maxdepth 1 -type f -name 'PCL2_Nex_*' -printf '%f\n' |
    LC_ALL=C sort
)
mapfile -t release_products < <(
  find "$release_assets_dir" -maxdepth 1 -type f -name 'PCL2_Nex_*' ! -name '*.asc' -printf '%f\n' |
    LC_ALL=C sort
)
if (( ${#release_assets[@]} == 0 )); then
  echo 'No PCL2_Nex release assets were downloaded.' >&2
  exit 1
fi
if (( ${#release_products[@]} == 0 )); then
  echo 'No signed release product was downloaded.' >&2
  exit 1
fi

expected_products=(
  "PCL2_Nex_${BUILD_CONFIGURATION}_win-x64.exe"
  "PCL2_Nex_${BUILD_CONFIGURATION}_win-arm64.exe"
  "PCL2_Nex_${BUILD_CONFIGURATION}_linux-x64.AppImage"
  "PCL2_Nex_${BUILD_CONFIGURATION}_linux-x64.deb"
  "PCL2_Nex_${BUILD_CONFIGURATION}_linux-x64.rpm"
  "PCL2_Nex_${BUILD_CONFIGURATION}_osx-x64.dmg"
  "PCL2_Nex_${BUILD_CONFIGURATION}_osx-arm64.dmg"
)
for product in "${expected_products[@]}"; do
  if [[ ! -s "$release_assets_dir/$product" ]]; then
    echo "Missing release product: $product" >&2
    exit 1
  fi
  if [[ ! -s "$release_assets_dir/$product.asc" ]]; then
    echo "Missing release signature: $product.asc" >&2
    exit 1
  fi
done
if (( ${#release_products[@]} != ${#expected_products[@]} )); then
  echo "Expected ${#expected_products[@]} release products, found ${#release_products[@]}." >&2
  printf 'Downloaded product: %s\n' "${release_products[@]}" >&2
  exit 1
fi
if (( ${#release_assets[@]} != ${#expected_products[@]} * 2 )); then
  echo "Expected one signature per release product, found ${#release_assets[@]} total release files." >&2
  printf 'Downloaded release file: %s\n' "${release_assets[@]}" >&2
  exit 1
fi
for asset in "${release_assets[@]}"; do
  test -s "$release_assets_dir/$asset"
done
(
  cd "$release_assets_dir"
  for asset in "${release_assets[@]}"; do
    sha256sum "$asset"
  done > SHA256SUMS
)

git lfs version

askpass="$RUNNER_TEMP/modelscope-askpass.sh"
mirror_dir="$RUNNER_TEMP/modelscope-release-mirror"
destination="releases/$RELEASE_TAG"
trap 'rm -f "$askpass"' EXIT
umask 077
cat > "$askpass" <<'EOF'
#!/usr/bin/env bash
case "$1" in
  *Username*) printf '%s\n' 'oauth2' ;;
  *Password*) printf '%s\n' "${MODELSCOPE_TOKEN:?}" ;;
  *) exit 1 ;;
esac
EOF
chmod 700 "$askpass"

export GIT_ASKPASS="$askpass"
export GIT_TERMINAL_PROMPT=0
export GIT_LFS_SKIP_SMUDGE=1
git clone --depth 1 --branch "$MODELSCOPE_BRANCH" \
  "https://www.modelscope.cn/datasets/$MODELSCOPE_DATASET.git" \
  "$mirror_dir"
git -C "$mirror_dir" lfs install --local
git -C "$mirror_dir" lfs pull --include "$destination/**"
git -C "$mirror_dir" lfs track '*.exe' '*.deb' '*.rpm' '*.AppImage' '*.dmg'

mkdir -p "$mirror_dir/$destination"
while IFS= read -r -d '' source; do
  name="$(basename "$source")"
  target="$mirror_dir/$destination/$name"
  if [[ -e "$target" ]] && ! cmp -s "$source" "$target"; then
    echo "Refusing to overwrite the existing release file: $destination/$name" >&2
    exit 1
  fi
  cp -- "$source" "$target"
done < <(find "$release_assets_dir" -maxdepth 1 -type f -name 'PCL2_Nex_*' -print0)
for file in SHA256SUMS; do
  source="$release_assets_dir/$file"
  target="$mirror_dir/$destination/$file"
  if [[ -e "$target" ]] && ! cmp -s "$source" "$target"; then
    echo "Refusing to overwrite the existing release file: $destination/$file" >&2
    exit 1
  fi
  cp -- "$source" "$target"
done
cp -- "$release_assets_dir/RELEASE_NOTE.md" "$mirror_dir/$destination/RELEASE_NOTE.md"

git -C "$mirror_dir" config user.name 'github-actions[bot]'
git -C "$mirror_dir" config user.email '41898282+github-actions[bot]@users.noreply.github.com'
git -C "$mirror_dir" add .gitattributes "$destination"
if git -C "$mirror_dir" diff --cached --quiet; then
  echo "ModelScope already contains release $RELEASE_TAG."
else
  git -C "$mirror_dir" commit -m "Mirror PCL2 Nex $RELEASE_TAG"
  for attempt in 1 2 3; do
    if git -C "$mirror_dir" push origin "HEAD:$MODELSCOPE_BRANCH"; then
      break
    fi
    if [[ "$attempt" -eq 3 ]]; then
      echo 'Unable to push the ModelScope mirror after three attempts.' >&2
      exit 1
    fi
    git -C "$mirror_dir" pull --rebase origin "$MODELSCOPE_BRANCH"
  done
fi

marker_start='<!-- pcl-nex-modelscope-mirror:start -->'
marker_end='<!-- pcl-nex-modelscope-mirror:end -->'
release_body="$RUNNER_TEMP/github-release-body.md"
release_body_without_mirror="$RUNNER_TEMP/github-release-body-without-mirror.md"
mirror_block="$RUNNER_TEMP/modelscope-release-links.md"
updated_body="$RUNNER_TEMP/github-release-body-with-modelscope.md"
modelscope_base="https://www.modelscope.cn/datasets/$MODELSCOPE_DATASET"
modelscope_tree_url="$modelscope_base/tree/$MODELSCOPE_BRANCH/$destination"
modelscope_download_base="$modelscope_base/resolve/$MODELSCOPE_BRANCH/$destination"
github_release_url="$GITHUB_SERVER_URL/$GITHUB_REPOSITORY/releases/tag/$RELEASE_TAG"
github_download_base="$GITHUB_SERVER_URL/$GITHUB_REPOSITORY/releases/download/$RELEASE_TAG"
github_asset_names="$RUNNER_TEMP/github-release-asset-names.txt"

gh release view "$RELEASE_TAG" \
  --repo "$GITHUB_REPOSITORY" \
  --json body \
  --jq '.body' > "$release_body"
gh release view "$RELEASE_TAG" \
  --repo "$GITHUB_REPOSITORY" \
  --json assets \
  --jq '.assets[].name' > "$github_asset_names"
awk -v start="$marker_start" -v end="$marker_end" '
  $0 == start { skipping = 1; next }
  $0 == end { skipping = 0; next }
  !skipping { print }
' "$release_body" > "$release_body_without_mirror"

{
  echo "$marker_start"
  echo '## ModelScope 下载镜像'
  echo
  echo "国内网络可优先使用 [ModelScope 镜像]($modelscope_tree_url)。以下链接是稳定入口，会自动跳转到当前可用的 CDN 地址。"
  echo
  echo '| 文件 | ModelScope 镜像 | GitHub |'
  echo '| --- | --- | --- |'
  while IFS= read -r asset; do
    case "$asset" in
      *.asc) continue ;;
    esac
    github_asset_url="$github_release_url"
    if grep -Fqx -- "$asset" "$github_asset_names"; then
      github_asset_url="$github_download_base/$asset"
    fi
    echo "| \`$asset\` | [下载]($modelscope_download_base/$asset) | [备用]($github_asset_url) |"
  done < <(printf '%s\n' "${release_assets[@]}" | LC_ALL=C sort)
  echo
  echo "[SHA-256 校验文件]($modelscope_download_base/SHA256SUMS)"
  echo "$marker_end"
} > "$mirror_block"

cat "$release_body_without_mirror" > "$updated_body"
if [[ -s "$release_body_without_mirror" ]]; then
  printf '\n\n' >> "$updated_body"
fi
cat "$mirror_block" >> "$updated_body"
gh release edit "$RELEASE_TAG" \
  --repo "$GITHUB_REPOSITORY" \
  --notes-file "$updated_body"

{
  echo '## ModelScope mirror'
  echo
  echo "Mirrored release $RELEASE_TAG to [$MODELSCOPE_DATASET]($modelscope_tree_url)."
} >> "$GITHUB_STEP_SUMMARY"
