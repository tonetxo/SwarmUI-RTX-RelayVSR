// 'RelayVSR Upscale' button in the history/media menu for videos.
(function() {
    // A stale saved value of the internal file param would turn every generation into an upscale.
    try {
        if (typeof deleteCookie === 'function') {
            deleteCookie('lastparam_input_relayvsrvideofile');
        }
        let fileElem = document.getElementById('input_relayvsrvideofile');
        if (fileElem) {
            fileElem.value = '';
        }
    }
    catch (e) {
        console.error('RelayVSR: could not clear the history file param:', e);
    }

    if (typeof registerMediaButton !== 'function') {
        console.warn('RelayVSR: registerMediaButton is not available, SwarmUI is too old');
        return;
    }

    registerMediaButton(
        'RelayVSR Upscale',
        (src) => {
            if (typeof currentBackendFeatureSet === 'undefined' || !currentBackendFeatureSet.includes('relayvsr')) {
                showError('RelayVSR is not available on the current backend: update ComfyUI (native RelayVSR nodes).');
                return;
            }
            if (src.startsWith('data:') || getMediaType(src) !== 'video') {
                return;
            }
            let input_overrides = { 'images': 1 };
            input_overrides['relayvsrvideofile'] = getImageOutPrefix() + '/' + getImageFullSrc(src);
            // Use the panel values even when the group toggle is off: pressing the button already says what to do.
            for (let paramId of ['relayvsrscale', 'relayvsrdetail', 'relayvsrdownscalemethod']) {
                let elem = document.getElementById('input_' + paramId);
                if (elem) {
                    let val = getInputVal(elem, true);
                    if (val != null && val !== '') {
                        input_overrides[paramId] = val;
                    }
                }
            }
            mainGenHandler.doGenerate(input_overrides, {});
        },
        'Upscale this video with RelayVSR (uses the RelayVSR Upscale panel values)',
        ['video'],
        true,
        true
    );
})();
