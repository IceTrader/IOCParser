'use strict';

document.addEventListener('DOMContentLoaded', () => {
    const dropzone = document.getElementById('dropzone');
    const fileInput = document.getElementById('fileInput');
    const errorBox = document.getElementById('error');
    const resultsContainer = document.getElementById('resultsContainer');
    const fileResultsList = document.getElementById('fileResultsList');
    const historyBox = document.getElementById('history');
    const uploadComment = document.getElementById('uploadComment');
    const clearResultsBtn = document.getElementById('clearResultsBtn');

    let isUploading = false; // Флаг, идет ли сейчас загрузка

    // Предупреждение при уходе со страницы во время загрузки
    window.addEventListener('beforeunload', (e) => {
        if (isUploading) {
            e.preventDefault();
            e.returnValue = '';
        }
    });

    // ==========================================
    // 1. БЕЗОПАСНОЕ СОХРАНЕНИЕ В LOCALSTORAGE
    // ==========================================
    function safeSaveState(state) {
        try {
            // Клонируем объект, чтобы не менять текущее состояние в памяти
            const stateToSave = JSON.parse(JSON.stringify(state));

            // Обрезаем тяжелые данные перед сохранением, чтобы не превысить лимит 5МБ
            stateToSave.files.forEach(f => {
                if (f.status === 'done' && f.data && f.data.indicators) {
                    // Оставляем только первые 20 IOC каждого типа для восстановления UI
                    f.data.indicators = Object.fromEntries(
                        Object.entries(f.data.indicators).map(([k, v]) => [k, v.slice(0, 20)])
                    );
                }
            });

            localStorage.setItem('ioc_upload_state', JSON.stringify(stateToSave));
        } catch (e) {
            console.warn("Не удалось сохранить состояние в localStorage (возможно, превышен лимит).", e);
            // Если места все равно нет, сохраняем только статусы (без таблиц)
            try {
                const minimalState = {
                    comment: state.comment,
                    files: state.files.map(f => ({ name: f.name, status: f.status, error: f.error }))
                };
                localStorage.setItem('ioc_upload_state', JSON.stringify(minimalState));
            } catch (e2) {
                console.error("Даже минимальное состояние не влезло в localStorage", e2);
            }
        }
    }

    // ==========================================
    // 2. ВОССТАНОВЛЕНИЕ СОСТОЯНИЯ
    // ==========================================
    function loadState() {
        const saved = localStorage.getItem('ioc_upload_state');
        if (!saved) return;

        try {
            const state = JSON.parse(saved);
            if (state.comment) uploadComment.value = state.comment;

            if (state.files && state.files.length > 0) {
                resultsContainer.style.display = 'block';
                clearResultsBtn.style.display = 'inline-block';
                fileResultsList.innerHTML = '';

                state.files.forEach(fileState => {
                    const block = createFileBlock(fileState.name);

                    if (fileState.status === 'done') {
                        updateBlockStatus(block, 'Готово', 'bg-success');
                        try {
                            if (fileState.data) renderFileResults(fileState.data, block.querySelector('.file-results'));
                        } catch (e) {
                            console.error("Ошибка рендера результатов при восстановлении", e);
                        }
                    } else if (fileState.status === 'error') {
                        updateBlockStatus(block, 'Ошибка', 'bg-danger');
                        const errDiv = block.querySelector('.file-error');
                        errDiv.textContent = fileState.error || 'Неизвестная ошибка';
                        errDiv.style.display = 'block';
                    } else {
                        updateBlockStatus(block, 'Прервано', 'bg-warning text-dark');
                        const body = block.querySelector('.card-body');
                        const retryBtn = document.createElement('button');
                        retryBtn.className = 'btn btn-sm btn-outline-warning mt-2';
                        retryBtn.textContent = 'Выбрать файл заново';
                        retryBtn.onclick = () => {
                            block.remove();
                            fileInput.click();
                        };
                        body.appendChild(retryBtn);
                    }
                });
            }
        } catch (e) {
            console.error("Ошибка восстановления состояния", e);
            localStorage.removeItem('ioc_upload_state');
        }
    }

    clearResultsBtn.addEventListener('click', () => {
        if (isUploading) {
            alert('Дождитесь окончания загрузки.');
            return;
        }
        localStorage.removeItem('ioc_upload_state');
        fileResultsList.innerHTML = '';
        resultsContainer.style.display = 'none';
        clearResultsBtn.style.display = 'none';
        uploadComment.value = '';
    });

    // ==========================================
    // 3. DRAG & DROP (с проверкой комментария)
    // ==========================================
    function checkComment() {
        if (!uploadComment.value.trim()) {
            alert('Пожалуйста, укажите комментарий к загрузке перед выбором файлов. Это поможет в будущем найти эти индикаторы.');
            uploadComment.focus();
            uploadComment.classList.add('is-invalid');
            setTimeout(() => uploadComment.classList.remove('is-invalid'), 2000);
            return false;
        }
        return true;
    }

    if (dropzone && fileInput) {
        dropzone.addEventListener('click', (e) => {
            // ЗАЩИТА ОТ БЕСКОНЕЧНОГО ЦИКЛА:
            // Если клик был вызван программно на fileInput, игнорируем его
            if (e.target === fileInput) return;

            if (checkComment()) {
                fileInput.click();
            }
        });

        fileInput.addEventListener('change', () => {
            if (fileInput.files.length > 0) {
                handleFiles(Array.from(fileInput.files));
                fileInput.value = ''; // Сброс
            }
        });

        ['dragenter', 'dragover'].forEach(evt =>
            dropzone.addEventListener(evt, e => {
                e.preventDefault(); e.stopPropagation();
                if (uploadComment.value.trim()) {
                    dropzone.classList.add('dragover');
                }
            })
        );

        ['dragleave', 'drop'].forEach(evt =>
            dropzone.addEventListener(evt, e => {
                e.preventDefault(); e.stopPropagation();
                dropzone.classList.remove('dragover');
            })
        );

        dropzone.addEventListener('drop', e => {
            if (e.dataTransfer.files.length > 0) {
                if (checkComment()) {
                    handleFiles(Array.from(e.dataTransfer.files));
                }
            }
        });
    }

    // ==========================================
    // 4. ОБРАБОТКА ФАЙЛОВ
    // ==========================================
    async function handleFiles(files) {
        if (!uploadComment.value.trim()) {
            checkComment();
            return;
        }

        isUploading = true;
        errorBox.style.display = 'none';
        resultsContainer.style.display = 'block';
        clearResultsBtn.style.display = 'inline-block';

        let state = JSON.parse(localStorage.getItem('ioc_upload_state') || '{"comment":"","files":[]}');
        state.comment = uploadComment.value;

        for (const file of files) {
            try {
                // УДАЛЯЕМ СТАРЫЕ ЗАПИСИ ДЛЯ ЭТОГО ФАЙЛА (если он уже был в списке)
                state.files = state.files.filter(f => f.name !== file.name);

                // УДАЛЯЕМ СТАРЫЙ DOM-БЛОК ЭТОГО ФАЙЛА (если он отображался)
                const oldBlock = fileResultsList.querySelector(`[data-filename="${CSS.escape(file.name)}"]`);
                if (oldBlock) oldBlock.remove();

                // Добавляем в состояние новую запись
                const fileEntry = { name: file.name, status: 'uploading', error: null, data: null };
                state.files.push(fileEntry);
                safeSaveState(state);

                // Запускаем загрузку
                await processSingleFile(file, fileEntry, state);
            } catch (err) {
                console.error("Критическая ошибка при обработке файла " + file.name, err);
            }
        }

        isUploading = false;
        loadHistory();
    }

    function createFileBlock(fileName) {
        const block = document.createElement('div');
        block.className = 'card mb-4 shadow-sm';
        block.setAttribute('data-filename', fileName); // <--- ДОБАВИТЬ ЭТУ СТРОКУ
        block.innerHTML = `
            <div class="card-header d-flex justify-content-between align-items-center">
                <span class="fw-bold text-truncate" style="max-width: 80%;">${escapeHtml(fileName)}</span>
                <span class="file-status badge bg-secondary">Ожидание...</span>
            </div>
            <div class="card-body">
                <div class="progress mb-2 file-progress" style="display:none;">
                    <div class="progress-bar progress-bar-striped progress-bar-animated" style="width: 0%;">0%</div>
                </div>
                <div class="file-error alert alert-danger" style="display:none;"></div>
                <div class="file-results" style="display:none;"></div>
            </div>
        `;
        fileResultsList.appendChild(block);
        return block;
    }

    function updateBlockStatus(block, text, badgeClass) {
        const statusBadge = block.querySelector('.file-status');
        if (statusBadge) {
            statusBadge.textContent = text;
            statusBadge.className = `file-status badge ${badgeClass}`;
        }
    }

    function processSingleFile(file, fileEntry, state) {
        return new Promise((resolve) => {
            const block = createFileBlock(file.name);
            const progressBox = block.querySelector('.file-progress');
            const progressBar = block.querySelector('.progress-bar');
            const errorDiv = block.querySelector('.file-error');
            const resultsDiv = block.querySelector('.file-results');

            const formData = new FormData();
            formData.append('file', file);
            if (uploadComment.value) formData.append('comment', uploadComment.value);

            const xhr = new XMLHttpRequest();
            xhr.open('POST', '/api/scan');

            updateBlockStatus(block, 'Загрузка...', 'bg-primary');
            progressBox.style.display = 'block';

            xhr.upload.onprogress = (e) => {
                if (e.lengthComputable) {
                    const pct = Math.round((e.loaded / e.total) * 100);
                    progressBar.style.width = pct + '%';
                    progressBar.textContent = pct + '%';
                    if (pct === 100) updateBlockStatus(block, 'Сканирование...', 'bg-primary');
                }
            };

            xhr.onload = () => {
                progressBox.style.display = 'none';

                if (xhr.status >= 200 && xhr.status < 300) {
                    try {
                        const data = JSON.parse(xhr.responseText);
                        updateBlockStatus(block, 'Готово', 'bg-success');

                        // Рендерим результаты
                        renderFileResults(data, resultsDiv);

                        fileEntry.status = 'done';
                        fileEntry.data = data;
                    } catch (e) {
                        console.error("Ошибка парсинга JSON или рендера", e);
                        updateBlockStatus(block, 'Ошибка данных', 'bg-danger');
                        fileEntry.status = 'error';
                        fileEntry.error = 'Сервер вернул некорректные данные';
                    }
                } else {
                    updateBlockStatus(block, 'Ошибка', 'bg-danger');
                    let errMsg = 'Ошибка сервера';
                    try { errMsg = JSON.parse(xhr.responseText).error || errMsg; } catch { }
                    errorDiv.textContent = errMsg;
                    errorDiv.style.display = 'block';

                    fileEntry.status = 'error';
                    fileEntry.error = errMsg;
                }

                safeSaveState(state);
                resolve();
            };

            xhr.onerror = () => {
                progressBox.style.display = 'none';
                updateBlockStatus(block, 'Ошибка сети', 'bg-danger');
                errorDiv.textContent = 'Ошибка сети. Проверьте соединение.';
                errorDiv.style.display = 'block';

                fileEntry.status = 'error';
                fileEntry.error = 'Ошибка сети';
                safeSaveState(state);
                resolve();
            };

            xhr.send(formData);
        });
    }

    // ==========================================
    // 5. РЕНДЕР РЕЗУЛЬТАТОВ (ОПТИМИЗИРОВАН)
    // ==========================================
    function renderFileResults(data, container) {
        if (!container) return;
        container.style.display = 'block';
        container.innerHTML = '';

        if (!data || data.totalCount === 0) {
            container.innerHTML = `<div class="alert alert-info mb-0">🎉 Ничего не найдено.</div>`;
            return;
        }

        // Используем DocumentFragment для быстрой вставки в DOM
        const fragment = document.createDocumentFragment();

        const summary = document.createElement('p');
        summary.className = 'mb-3 text-muted small';
        summary.innerHTML = `Найдено: <strong>${data.totalCount}</strong> · Время: ${data.processingTimeMs} мс · ID в БД: <code>${data.documentId || '—'}</code>`;
        fragment.appendChild(summary);

        const downloadAll = document.createElement('button');
        downloadAll.className = 'btn btn-sm btn-success mb-3';
        downloadAll.textContent = 'Скачать всё (ZIP)';
        downloadAll.onclick = () => window.location.href = `/api/scan/download-zip?scanId=${data.scanId}`;
        fragment.appendChild(downloadAll);

        if (data.indicators) {
            Object.keys(data.indicators).sort().forEach(type => {
                const values = data.indicators[type];

                const details = document.createElement('details');
                details.className = 'indicator-details mb-2';

                const summaryEl = document.createElement('summary');
                summaryEl.innerHTML = `<strong>${type.toUpperCase()}</strong> — <span class="badge bg-secondary">${values.length}</span>`;
                details.appendChild(summaryEl);

                const dlBtn = document.createElement('button');
                dlBtn.className = 'btn btn-sm btn-outline-primary mt-2 mb-2';
                dlBtn.textContent = `Скачать ${type.toUpperCase()}`;
                dlBtn.onclick = (e) => {
                    e.preventDefault();
                    window.location.href = `/api/scan/download?scanId=${data.scanId}&type=${type}`;
                };
                details.appendChild(dlBtn);

                const table = document.createElement('table');
                table.className = 'table table-sm table-striped table-bordered mt-2';
                const tbody = document.createElement('tbody');

                // Ограничиваем до 50 строк, чтобы не вешать браузер
                values.slice(0, 50).forEach(item => {
                    const tr = document.createElement('tr');
                    const td1 = document.createElement('td');
                    td1.textContent = item.value;
                    const td2 = document.createElement('td');
                    td2.textContent = item.vendor || '—';
                    tr.appendChild(td1);
                    tr.appendChild(td2);
                    tbody.appendChild(tr);
                });
                table.appendChild(tbody);
                details.appendChild(table);

                if (values.length > 50) {
                    const note = document.createElement('p');
                    note.className = 'text-muted small';
                    note.textContent = `Показаны первые 50 из ${values.length}. Скачайте ZIP, чтобы увидеть все.`;
                    details.appendChild(note);
                }

                fragment.appendChild(details);
            });
        }

        container.appendChild(fragment);
    }

    // ==========================================
    // 6. ИСТОРИЯ СКАНИРОВАНИЙ
    // ==========================================
    function loadHistory() {
        if (!historyBox) return;
        fetch('/api/scan/history?limit=20')
            .then(r => r.json())
            .then(items => renderHistory(items))
            .catch(() => { historyBox.innerHTML = '<p class="text-muted">Не удалось загрузить историю.</p>'; });
    }

    function renderHistory(items) {
        if (!items || items.length === 0) {
            historyBox.innerHTML = '<p class="text-muted">Пока нет ни одного сканирования.</p>';
            return;
        }
        const table = document.createElement('table');
        table.className = 'table table-sm table-hover';
        table.innerHTML = `<thead><tr><th>Файл</th><th>Дата</th><th>Индикаторов</th><th>Время</th><th></th></tr></thead>`;
        const tbody = document.createElement('tbody');
        items.forEach(item => {
            const tr = document.createElement('tr');
            tr.innerHTML = `
                <td>${escapeHtml(item.fileName)}</td>
                <td>${new Date(item.scannedAt).toLocaleString()}</td>
                <td>${item.totalCount}</td>
                <td>${item.processingTimeMs} мс</td>
                <td><a class="btn btn-sm btn-outline-primary" href="/api/scan/download-zip?scanId=${item.scanId}">Скачать</a></td>`;
            tbody.appendChild(tr);
        });
        table.appendChild(tbody);
        historyBox.innerHTML = '';
        historyBox.appendChild(table);
    }

    // ==========================================
    // 7. ПРОВЕРКА IP (AbuseIPDB + proxycheck.io)
    // ==========================================
    const ipCheckBtn = document.getElementById('ipCheckBtn');
    if (ipCheckBtn) {
        ipCheckBtn.addEventListener('click', () => {
            const ip = document.getElementById('ipInput').value.trim();
            const box = document.getElementById('ipCheckResult');
            if (!ip) {
                box.innerHTML = '<span class="text-muted">Введите IP.</span>';
                return;
            }

            box.innerHTML = '<span class="text-muted">Проверка через AbuseIPDB и proxycheck.io...</span>';
            fetch(`/api/ipcheck?ip=${encodeURIComponent(ip)}`)
                .then(r => r.json())
                .then(data => {
                    if (data.error) {
                        box.innerHTML = `<div class="alert alert-warning">${escapeHtml(data.error)}</div>`;
                        return;
                    }

                    const cls = data.isRisky ? 'danger' : 'success';
                    const abuse = data.abuseipdb || {};
                    const proxy = data.proxycheck || {};

                    box.innerHTML = `
                        <div class="alert alert-${cls}">
                            <h5><strong>${escapeHtml(data.ip)}</strong> — ${data.isRisky ? '⚠️ ОПАСНЫЙ' : '✅ чистый'}</h5>

                            <h6>AbuseIPDB</h6>
                            <div>
                                Abuse Score: <strong>${abuse.abuseConfidence ?? '—'}</strong> / 100<br />
                                Жалоб: ${abuse.totalReports ?? 0}<br />
                                ISP: ${escapeHtml(abuse.isp ?? '—')} · Страна: ${escapeHtml(abuse.country ?? '—')}<br />
                                Whitelisted: ${abuse.isWhitelisted ? 'да' : 'нет'}
                                ${abuse.error ? `<br /><span class="text-warning">Ошибка: ${escapeHtml(abuse.error)}</span>` : ''}
                            </div>

                            <h6 class="mt-3">proxycheck.io</h6>
                            <div>
                                Proxy/VPN: <strong>${proxy.isProxy ? 'да' : 'нет'}</strong><br />
                                Тип: ${escapeHtml(proxy.type ?? '—')}<br />
                                Risk: ${proxy.risk ?? '—'} / 100<br />
                                Провайдер: ${escapeHtml(proxy.provider ?? '—')}<br />
                                ASN: ${escapeHtml(proxy.asn ?? '—')} · Страна: ${escapeHtml(proxy.country ?? '—')}
                                ${proxy.error ? `<br /><span class="text-warning">Ошибка: ${escapeHtml(proxy.error)}</span>` : ''}
                            </div>
                        </div>`;
                })
                .catch(err => {
                    box.innerHTML = `<div class="alert alert-danger">${escapeHtml(err.message)}</div>`;
                });
        });
    }

    // ==========================================
    // 8. ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ
    // ==========================================
    function escapeHtml(s) {
        if (!s) return '';
        return String(s).replace(/[&<>"']/g, c => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }

    // ==========================================
    // 9. ИНИЦИАЛИЗАЦИЯ
    // ==========================================
    loadState();
    loadHistory();
});