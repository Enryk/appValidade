// barcodeScanner.js - ValiData Camera Barcode Reader
window.validataScanner = {
    stream: null,
    animationFrameId: null,
    html5QrCode: null,
    ativo: false,

    beep: function() {
        try {
            const AudioContext = window.AudioContext || window.webkitAudioContext;
            if (!AudioContext) return;
            const ctx = new AudioContext();
            const osc = ctx.createOscillator();
            const gain = ctx.createGain();
            osc.type = 'sine';
            osc.frequency.setValueAtTime(1400, ctx.currentTime);
            gain.gain.setValueAtTime(0.25, ctx.currentTime);
            gain.gain.exponentialRampToValueAtTime(0.01, ctx.currentTime + 0.12);
            osc.connect(gain);
            gain.connect(ctx.destination);
            osc.start();
            osc.stop(ctx.currentTime + 0.12);
        } catch (e) {
            // áudio não permitido ou erro
        }
        if (navigator.vibrate) {
            try { navigator.vibrate(70); } catch(e) {}
        }
    },

    iniciar: async function(dotNetHelper, videoElementId, containerId) {
        window.validataScanner.parar();
        window.validataScanner.ativo = true;

        const video = document.getElementById(videoElementId);
        if (!video) return false;

        // Tenta 1: BarcodeDetector Nativo (Ultrarrápido, Chrome/Edge/Safari moderno)
        if ('BarcodeDetector' in window) {
            try {
                const stream = await navigator.mediaDevices.getUserMedia({
                    video: {
                        facingMode: { ideal: 'environment' },
                        width: { ideal: 1280 },
                        height: { ideal: 720 }
                    },
                    audio: false
                });

                window.validataScanner.stream = stream;
                video.srcObject = stream;
                await video.play();

                const detector = new BarcodeDetector({
                    formats: ['ean_13', 'ean_8', 'code_128', 'code_39', 'upc_a', 'upc_e', 'qr_code']
                });

                const scanFrame = async () => {
                    if (!window.validataScanner.ativo) return;

                    if (video.readyState === video.HAVE_ENOUGH_DATA) {
                        try {
                            const barcodes = await detector.detect(video);
                            if (barcodes && barcodes.length > 0) {
                                const code = barcodes[0].rawValue;
                                if (code && code.trim().length > 0) {
                                    window.validataScanner.beep();
                                    window.validataScanner.parar();
                                    dotNetHelper.invokeMethodAsync('OnCodigoBarrasDetectado', code.trim());
                                    return;
                                }
                            }
                        } catch (err) {}
                    }
                    window.validataScanner.animationFrameId = requestAnimationFrame(scanFrame);
                };

                window.validataScanner.animationFrameId = requestAnimationFrame(scanFrame);
                return true;
            } catch (err) {
                console.warn('Erro ao usar BarcodeDetector nativo, tentando fallback:', err);
            }
        }

        // Tenta 2: Html5Qrcode Library (Fallback universal para qualquer navegador)
        if (window.Html5Qrcode && containerId) {
            try {
                const qr = new Html5Qrcode(containerId);
                window.validataScanner.html5QrCode = qr;

                const config = {
                    fps: 15,
                    qrbox: { width: 260, height: 180 },
                    aspectRatio: 1.0
                };

                await qr.start(
                    { facingMode: 'environment' },
                    config,
                    (decodedText) => {
                        if (decodedText && window.validataScanner.ativo) {
                            window.validataScanner.beep();
                            window.validataScanner.parar();
                            dotNetHelper.invokeMethodAsync('OnCodigoBarrasDetectado', decodedText.trim());
                        }
                    },
                    (errorMessage) => {}
                );
                return true;
            } catch (err) {
                console.error('Erro ao iniciar Html5Qrcode:', err);
                return false;
            }
        }

        return false;
    },

    parar: function() {
        window.validataScanner.ativo = false;

        if (window.validataScanner.animationFrameId) {
            cancelAnimationFrame(window.validataScanner.animationFrameId);
            window.validataScanner.animationFrameId = null;
        }

        if (window.validataScanner.stream) {
            window.validataScanner.stream.getTracks().forEach(track => track.stop());
            window.validataScanner.stream = null;
        }

        if (window.validataScanner.html5QrCode) {
            try {
                window.validataScanner.html5QrCode.stop().then(() => {
                    window.validataScanner.html5QrCode.clear();
                    window.validataScanner.html5QrCode = null;
                }).catch(() => {
                    window.validataScanner.html5QrCode = null;
                });
            } catch (e) {
                window.validataScanner.html5QrCode = null;
            }
        }
    }
};
