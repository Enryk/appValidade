// barcodeScanner.js - ValiData Camera Barcode Reader (Mobile-first, iOS Safari & Android Chrome)
window.validataScanner = {
    html5QrCode: null,
    directStream: null,
    animationFrameId: null,
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
        } catch (e) {}
        if (navigator.vibrate) {
            try { navigator.vibrate(70); } catch(e) {}
        }
    },

    iniciar: async function(dotNetHelper, containerId) {
        await window.validataScanner.parar();
        window.validataScanner.ativo = true;

        const setStatus = (msg) => {
            try {
                dotNetHelper.invokeMethodAsync('OnScannerStatus', msg);
            } catch(e) {}
        };

        setStatus('Aguardando visor...');

        // 1. Aguarda elemento container estar montado no DOM
        let container = document.getElementById(containerId);
        let tentativas = 0;
        while (!container && tentativas < 25) {
            await new Promise(r => setTimeout(r, 100));
            container = document.getElementById(containerId);
            tentativas++;
        }

        if (!container) {
            setStatus('Erro: Visor não encontrado no DOM');
            return false;
        }

        container.innerHTML = '';

        // 2. Tenta obter a classe Html5Qrcode
        let Html5QrcodeClass = window.Html5Qrcode;
        if (!Html5QrcodeClass && window.__Html5QrcodeLibrary__) {
            Html5QrcodeClass = window.__Html5QrcodeLibrary__.Html5Qrcode;
        }

        // 3. ESTRATÉGIA A: Html5Qrcode Library
        if (Html5QrcodeClass) {
            try {
                setStatus('Detectando câmeras do dispositivo...');
                let cameraConfig = { facingMode: 'environment' };

                try {
                    const cameras = await Html5QrcodeClass.getCameras();
                    if (cameras && cameras.length > 0) {
                        const backCamera = cameras.find(c => /back|traseira|environment|rear|trás/i.test(c.label));
                        if (backCamera) {
                            cameraConfig = backCamera.id;
                        } else if (cameras.length > 1) {
                            cameraConfig = cameras[cameras.length - 1].id;
                        } else {
                            cameraConfig = cameras[0].id;
                        }
                    }
                } catch(camErr) {
                    // Fallback para restrição padrão
                    cameraConfig = { facingMode: 'environment' };
                }

                setStatus('Iniciando sensor de vídeo...');

                const qr = new Html5QrcodeClass(containerId, {
                    formatsToSupport: [
                        0,  // QR_CODE
                        2,  // CODABAR
                        3,  // CODE_39
                        5,  // CODE_128
                        8,  // ITF
                        9,  // EAN_13
                        10, // EAN_8
                        14, // UPC_A
                        15  // UPC_E
                    ],
                    verbose: false
                });

                window.validataScanner.html5QrCode = qr;

                const scanConfig = {
                    fps: 15,
                    qrbox: function(viewfinderWidth, viewfinderHeight) {
                        const w = Math.min(Math.floor(viewfinderWidth * 0.88), 340);
                        const h = Math.min(Math.floor(viewfinderHeight * 0.55), 180);
                        return { width: Math.max(w, 200), height: Math.max(h, 120) };
                    }
                };

                const onScanSuccess = async (decodedText) => {
                    if (decodedText && window.validataScanner.ativo) {
                        window.validataScanner.ativo = false;
                        window.validataScanner.beep();
                        await window.validataScanner.parar();
                        dotNetHelper.invokeMethodAsync('OnCodigoBarrasDetectado', decodedText.trim());
                    }
                };

                await qr.start(
                    cameraConfig,
                    scanConfig,
                    onScanSuccess,
                    () => {}
                );

                // Garante reprodução de vídeo sem bloqueio no iOS Safari
                const videoEl = container.querySelector('video');
                if (videoEl) {
                    videoEl.setAttribute('playsinline', 'true');
                    videoEl.setAttribute('webkit-playsinline', 'true');
                    videoEl.muted = true;
                    videoEl.autoplay = true;
                    videoEl.play().catch(() => {});
                }

                setStatus('Câmera conectada. Aponte para o código.');
                return true;
            } catch(html5Err) {
                console.warn('[ValiData Scanner] Falha ao iniciar Html5Qrcode, ativando fallback nativo:', html5Err);
                setStatus('Ativando modo de câmera nativo...');
            }
        }

        // 4. ESTRATÉGIA B: Fallback Direto WebRTC (Garante que a imagem NUNCA fique preta no Safari/iOS)
        try {
            await window.validataScanner.parar();
            window.validataScanner.ativo = true;
            container.innerHTML = '';

            setStatus('Solicitando acesso direto à câmera...');

            const stream = await navigator.mediaDevices.getUserMedia({
                video: {
                    facingMode: { ideal: 'environment' },
                    width: { ideal: 1280 },
                    height: { ideal: 720 }
                },
                audio: false
            });

            window.validataScanner.directStream = stream;

            const video = document.createElement('video');
            video.setAttribute('playsinline', 'true');
            video.setAttribute('webkit-playsinline', 'true');
            video.muted = true;
            video.autoplay = true;
            video.style.width = '100%';
            video.style.height = '100%';
            video.style.objectFit = 'cover';
            video.style.display = 'block';

            container.appendChild(video);
            video.srcObject = stream;

            await video.play();

            setStatus('Câmera ativa! Aponte para o código.');

            // Tenta decodificação se BarcodeDetector estiver disponível
            if ('BarcodeDetector' in window) {
                try {
                    const formats = ['ean_13', 'ean_8', 'code_128', 'code_39', 'upc_a', 'upc_e', 'qr_code'];
                    const detector = new BarcodeDetector({ formats: formats });

                    const scanLoop = async () => {
                        if (!window.validataScanner.ativo) return;
                        if (video.readyState >= 2) {
                            try {
                                const codes = await detector.detect(video);
                                if (codes && codes.length > 0) {
                                    const raw = codes[0].rawValue;
                                    if (raw && raw.trim().length > 0) {
                                        window.validataScanner.beep();
                                        await window.validataScanner.parar();
                                        dotNetHelper.invokeMethodAsync('OnCodigoBarrasDetectado', raw.trim());
                                        return;
                                    }
                                }
                            } catch(e) {}
                        }
                        window.validataScanner.animationFrameId = requestAnimationFrame(scanLoop);
                    };
                    window.validataScanner.animationFrameId = requestAnimationFrame(scanLoop);
                } catch(detErr) {
                    console.warn('[ValiData Scanner] BarcodeDetector nativo indisponível:', detErr);
                }
            }

            return true;
        } catch(fallbackErr) {
            console.error('[ValiData Scanner] Falha em todos os modos:', fallbackErr);
            setStatus('Erro na câmera: ' + (fallbackErr.message || 'Permissão negada'));
            return false;
        }
    },

    parar: async function() {
        window.validataScanner.ativo = false;

        if (window.validataScanner.animationFrameId) {
            cancelAnimationFrame(window.validataScanner.animationFrameId);
            window.validataScanner.animationFrameId = null;
        }

        if (window.validataScanner.directStream) {
            try {
                window.validataScanner.directStream.getTracks().forEach(t => t.stop());
            } catch(e) {}
            window.validataScanner.directStream = null;
        }

        if (window.validataScanner.html5QrCode) {
            const qr = window.validataScanner.html5QrCode;
            window.validataScanner.html5QrCode = null;
            try {
                if (qr.isScanning) {
                    await qr.stop();
                }
                qr.clear();
            } catch (e) {}
        }
    }
};
