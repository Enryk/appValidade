// barcodeScanner.js - ValiData Camera Barcode Reader (Mobile-first, iOS Safari & Android Chrome)
window.validataScanner = {
    stream: null,
    scanLoopId: null,
    scanTimerId: null,
    ativo: false,
    html5QrCode: null,

    beep: function() {
        try {
            const AudioContext = window.AudioContext || window.webkitAudioContext;
            if (AudioContext) {
                const ctx = new AudioContext();
                const osc = ctx.createOscillator();
                const gain = ctx.createGain();
                osc.type = 'sine';
                osc.frequency.setValueAtTime(1200, ctx.currentTime);
                osc.frequency.exponentialRampToValueAtTime(1800, ctx.currentTime + 0.08);
                gain.gain.setValueAtTime(0.3, ctx.currentTime);
                gain.gain.exponentialRampToValueAtTime(0.01, ctx.currentTime + 0.14);
                osc.connect(gain);
                gain.connect(ctx.destination);
                osc.start();
                osc.stop(ctx.currentTime + 0.14);
            }
        } catch (e) {}

        if (navigator.vibrate) {
            try { navigator.vibrate([70, 30, 70]); } catch(e) {}
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

        // 1. Aguarda container montado no DOM
        let container = document.getElementById(containerId);
        let tentativas = 0;
        while (!container && tentativas < 25) {
            await new Promise(r => setTimeout(r, 100));
            container = document.getElementById(containerId);
            tentativas++;
        }

        if (!container) {
            setStatus('Erro: Visor da câmera não encontrado');
            return false;
        }

        container.innerHTML = '';

        // Callback de sucesso ao encontrar código
        const triggerSuccess = async (rawCode) => {
            if (!window.validataScanner.ativo) return;
            window.validataScanner.ativo = false;
            const cleanCode = (rawCode || '').trim();
            if (!cleanCode) return;

            setStatus('Código lido: ' + cleanCode);
            window.validataScanner.beep();
            await window.validataScanner.parar();
            try {
                dotNetHelper.invokeMethodAsync('OnCodigoBarrasDetectado', cleanCode);
            } catch(err) {
                console.error('[ValiData Scanner] Erro ao invocar OnCodigoBarrasDetectado:', err);
            }
        };

        // 2. ESTRATÉGIA PRINCIPAL: WebRTC Direto com ZXing + BarcodeDetector
        // Garante que o stream de vídeo NUNCA fique preto no iOS Safari e decodifica retail EAN-13
        try {
            setStatus('Iniciando câmera...');

            let stream = null;
            const cameraConfigs = [
                {
                    video: {
                        facingMode: { ideal: 'environment' },
                        width: { ideal: 1280 },
                        height: { ideal: 720 }
                    },
                    audio: false
                },
                {
                    video: { facingMode: 'environment' },
                    audio: false
                },
                {
                    video: true,
                    audio: false
                }
            ];

            for (const cfg of cameraConfigs) {
                try {
                    stream = await navigator.mediaDevices.getUserMedia(cfg);
                    if (stream) break;
                } catch(e) {
                    // Tenta a próxima restrição
                }
            }

            if (!stream) {
                throw new Error('Não foi possível obter permissão ou sinal da câmera.');
            }

            window.validataScanner.stream = stream;

            // Tenta ativar foco contínuo se o hardware da câmera suportar
            try {
                const track = stream.getVideoTracks()[0];
                if (track && track.getCapabilities && track.applyConstraints) {
                    const capabilities = track.getCapabilities();
                    if (capabilities.focusMode && capabilities.focusMode.includes('continuous')) {
                        await track.applyConstraints({ advanced: [{ focusMode: 'continuous' }] });
                    }
                }
            } catch(focusErr) {}

            // Cria elemento <video> com todas as flags necessárias para iOS Safari & Android
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

            setStatus('Câmera pronta! Aponte para o código de barras.');

            // Prepara leitor nativo BarcodeDetector (se suportado no navegador)
            let nativeDetector = null;
            if ('BarcodeDetector' in window) {
                try {
                    nativeDetector = new BarcodeDetector({
                        formats: ['ean_13', 'ean_8', 'code_128', 'code_39', 'upc_a', 'upc_e', 'itf', 'qr_code']
                    });
                } catch(detErr) {
                    try {
                        nativeDetector = new BarcodeDetector({ formats: ['ean_13', 'code_128', 'qr_code'] });
                    } catch(detErr2) {
                        try {
                            nativeDetector = new BarcodeDetector({ formats: ['qr_code'] });
                        } catch(detErr3) {}
                    }
                }
            }

            // Prepara leitor ZXing multi-formato (pure JavaScript)
            let zxingReader = null;
            if (window.ZXing && window.ZXing.MultiFormatReader) {
                try {
                    zxingReader = new window.ZXing.MultiFormatReader();
                    const hints = new Map();
                    const formats = [
                        window.ZXing.BarcodeFormat.EAN_13,
                        window.ZXing.BarcodeFormat.EAN_8,
                        window.ZXing.BarcodeFormat.CODE_128,
                        window.ZXing.BarcodeFormat.CODE_39,
                        window.ZXing.BarcodeFormat.UPC_A,
                        window.ZXing.BarcodeFormat.UPC_E,
                        window.ZXing.BarcodeFormat.ITF,
                        window.ZXing.BarcodeFormat.QR_CODE
                    ].filter(f => f !== undefined && f !== null);

                    hints.set(window.ZXing.DecodeHintType.POSSIBLE_FORMATS, formats);
                    hints.set(window.ZXing.DecodeHintType.TRY_HARDER, true);
                    zxingReader.setHints(hints);
                } catch(zErr) {
                    console.warn('[ValiData Scanner] Erro ao instanciar ZXing MultiFormatReader:', zErr);
                }
            }

            // Canvas em memória para amostragem dos quadros (fora do DOM)
            const canvasFull = document.createElement('canvas');
            const ctxFull = canvasFull.getContext('2d', { willReadFrequently: true });

            const canvasCrop = document.createElement('canvas');
            const ctxCrop = canvasCrop.getContext('2d', { willReadFrequently: true });

            const decodeWithZxing = (cvs, reader, useGlobal) => {
                if (!reader || !window.ZXing) return null;
                try {
                    const lum = new window.ZXing.HTMLCanvasElementLuminanceSource(cvs);
                    const binarizer = useGlobal
                        ? new window.ZXing.GlobalHistogramBinarizer(lum)
                        : new window.ZXing.HybridBinarizer(lum);
                    const bitmap = new window.ZXing.BinaryBitmap(binarizer);
                    const res = reader.decode(bitmap);
                    if (res && res.getText) {
                        const txt = res.getText();
                        if (txt && txt.trim().length > 0) return txt.trim();
                    }
                } catch(e) {}
                return null;
            };

            // Loop contínuo de decodificação
            let frameCount = 0;
            let isScanningFrame = false;

            const processFrame = async () => {
                if (!window.validataScanner.ativo) return;

                if (!isScanningFrame && video.readyState >= 2 && video.videoWidth > 0) {
                    isScanningFrame = true;
                    frameCount++;

                    const vw = video.videoWidth;
                    const vh = video.videoHeight;

                    try {
                        // 1. Tenta decodificação nativa por hardware primeiro
                        if (nativeDetector) {
                            try {
                                const barcodes = await nativeDetector.detect(video);
                                if (barcodes && barcodes.length > 0) {
                                    const code = barcodes[0].rawValue;
                                    if (code && code.trim().length > 0) {
                                        await triggerSuccess(code);
                                        return;
                                    }
                                }
                            } catch(natErr) {}
                        }

                        // 2. Decodificação com ZXing (Full Frame)
                        if (zxingReader) {
                            // Redimensiona proporcionalmente para máximo 960px de largura (ótimo para 1D barcode)
                            let scale = 1;
                            if (vw > 960) scale = 960 / vw;
                            const sw = Math.floor(vw * scale);
                            const sh = Math.floor(vh * scale);

                            if (canvasFull.width !== sw || canvasFull.height !== sh) {
                                canvasFull.width = sw;
                                canvasFull.height = sh;
                            }

                            ctxFull.drawImage(video, 0, 0, sw, sh);

                            // Alterna ou tenta Hybrid e Global Histogram
                            let detectedText = decodeWithZxing(canvasFull, zxingReader, false);
                            if (!detectedText) {
                                detectedText = decodeWithZxing(canvasFull, zxingReader, true);
                            }

                            // 3. Se ainda não detectou, foca na faixa central de mira (resolução nativa 1:1)
                            if (!detectedText && vw >= 800) {
                                const cropW = Math.floor(vw * 0.85);
                                const cropH = Math.floor(vh * 0.45);
                                const cropX = Math.floor((vw - cropW) / 2);
                                const cropY = Math.floor((vh - cropH) / 2);

                                if (canvasCrop.width !== cropW || canvasCrop.height !== cropH) {
                                    canvasCrop.width = cropW;
                                    canvasCrop.height = cropH;
                                }

                                ctxCrop.drawImage(video, cropX, cropY, cropW, cropH, 0, 0, cropW, cropH);
                                detectedText = decodeWithZxing(canvasCrop, zxingReader, true) ||
                                               decodeWithZxing(canvasCrop, zxingReader, false);
                            }

                            if (detectedText) {
                                await triggerSuccess(detectedText);
                                return;
                            }
                        }
                    } catch(frameErr) {
                        // Quadro sem código legível, segue para o próximo
                    } finally {
                        isScanningFrame = false;
                    }
                }

                if (window.validataScanner.ativo) {
                    window.validataScanner.scanTimerId = setTimeout(processFrame, 75);
                }
            };

            // Inicia loop de leitura
            window.validataScanner.scanTimerId = setTimeout(processFrame, 150);
            return true;

        } catch(directErr) {
            console.warn('[ValiData Scanner] Erro no WebRTC direto, tentando Html5Qrcode:', directErr);
        }

        // 3. ESTRATÉGIA SECUNDÁRIA: Html5Qrcode Library (se WebRTC direto falhar)
        const Html5QrcodeClass = window.Html5Qrcode || (window.__Html5QrcodeLibrary__ && window.__Html5QrcodeLibrary__.Html5Qrcode);
        if (Html5QrcodeClass) {
            try {
                setStatus('Iniciando modo alternativo...');
                const qr = new Html5QrcodeClass(containerId, {
                    useBarCodeDetectorIfSupported: false, // Força uso de ZXing
                    verbose: false
                });
                window.validataScanner.html5QrCode = qr;

                await qr.start(
                    { facingMode: 'environment' },
                    { fps: 12 },
                    (decodedText) => triggerSuccess(decodedText),
                    () => {}
                );

                const v = container.querySelector('video');
                if (v) {
                    v.setAttribute('playsinline', 'true');
                    v.setAttribute('webkit-playsinline', 'true');
                    v.muted = true;
                    v.autoplay = true;
                    v.play().catch(() => {});
                }

                setStatus('Câmera pronta! Aponte para o código.');
                return true;
            } catch(hErr) {
                console.error('[ValiData Scanner] Falha também no Html5Qrcode:', hErr);
                setStatus('Erro ao acessar a câmera: ' + (hErr.message || hErr));
                return false;
            }
        }

        setStatus('Erro: Câmera indisponível no dispositivo.');
        return false;
    },

    parar: async function() {
        window.validataScanner.ativo = false;

        if (window.validataScanner.scanTimerId) {
            clearTimeout(window.validataScanner.scanTimerId);
            window.validataScanner.scanTimerId = null;
        }

        if (window.validataScanner.scanLoopId) {
            cancelAnimationFrame(window.validataScanner.scanLoopId);
            window.validataScanner.scanLoopId = null;
        }

        if (window.validataScanner.stream) {
            try {
                window.validataScanner.stream.getTracks().forEach(t => t.stop());
            } catch(e) {}
            window.validataScanner.stream = null;
        }

        if (window.validataScanner.html5QrCode) {
            const qr = window.validataScanner.html5QrCode;
            window.validataScanner.html5QrCode = null;
            try {
                if (qr.isScanning) {
                    await qr.stop();
                }
                qr.clear();
            } catch(e) {}
        }
    }
};
