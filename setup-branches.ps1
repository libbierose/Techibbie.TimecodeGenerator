# setup-branches.ps1
# One-time script to initialise the dev/release branch structure.
# Run from the project root AFTER the repo already exists on GitHub:
#
#   .\setup-branches.ps1
#
# What it does:
#   1. Renames the current branch to `dev` (if it isn't already).
#   2. Creates the `release` branch from `dev`.
#   3. Pushes both branches and sets the upstream tracking.
#   4. Sets `dev` as the default working branch.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$currentBranch = git rev-parse --abbrev-ref HEAD

# ── Step 1: ensure we're on dev ──────────────────────────────────────────────
if ($currentBranch -ne 'dev') {
    Write-Host "Renaming branch '$currentBranch' → 'dev'..."
    git branch -m $currentBranch dev
}

# ── Step 2: create release from dev ──────────────────────────────────────────
$branches = git branch --list release
if ($branches) {
    Write-Host "'release' branch already exists — skipping creation."
} else {
    Write-Host "Creating 'release' branch from 'dev'..."
    git branch release dev
}

# ── Step 3: push both branches ───────────────────────────────────────────────
Write-Host "Pushing 'dev' to origin..."
git push --set-upstream origin dev

Write-Host "Pushing 'release' to origin..."
git push --set-upstream origin release

# ── Step 4: switch back to dev ───────────────────────────────────────────────
git checkout dev

Write-Host ""
Write-Host "Done. Branch structure:"
Write-Host "  dev     — active development, CI checks on every push"
Write-Host "  release — stable code, builds + publishes binaries on every push/merge"
Write-Host ""
Write-Host "Set 'dev' as the default branch in GitHub:"
Write-Host "  Settings → Branches → Default branch → switch to 'dev'"
