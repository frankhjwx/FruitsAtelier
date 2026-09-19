// Download links work without this request; metadata is optional on restricted networks.
(async () => {
  try {
    const response = await fetch('https://api.github.com/repos/frankhjwx/FruitsAtelier/releases/latest', {
      signal: AbortSignal.timeout(5000)
    });
    if (!response.ok) return;
    const release = await response.json();
    if (release.draft || release.prerelease || !/^v\d+\.\d+\.\d+$/.test(release.tag_name)) return;
    const asset = release.assets?.find(asset => asset.name === 'FruitsAtelier-win-x64.zip' && asset.state === 'uploaded');
    if (!asset || !Number.isSafeInteger(asset.size) || asset.size <= 0) return;
    document.querySelectorAll('[data-release-version]').forEach(node => { node.textContent = release.tag_name; });
    document.querySelectorAll('[data-release-size]').forEach(node => { node.textContent = `${(asset.size / 1024 / 1024).toFixed(1)} MB ZIP`; });
  } catch {
    // Keep the latest-release download usable when metadata is unavailable.
  }
})();
