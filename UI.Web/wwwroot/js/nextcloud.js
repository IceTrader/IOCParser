'use strict';

document.addEventListener('DOMContentLoaded', () => {
    const syncBtn = document.getElementById('syncBtn');
    if (!syncBtn) return;

    const previewSection = document.getElementById('previewSection');
    const previewFilesList = document.getElementById('previewFilesList');

    let currentSyncId = null;
    let pollHandle = null;

    syncBtn.addEventListener('click', async () => {
        syncBtn.disabled = true;
        syncBtn.textContent = 'Запуск...';

        // Очищаем старые данные и прогресс-бар
        previewSection.style.display = 'none';
        const oldProgress = document.getElementById('syncProgressContainer');
        if (oldProgress) oldProgress.remove();

        try {
            const response = await fetch('/api/nextcloud/prepare', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({})
            });

            const data = await response.json();
            if (!response.ok) throw new Error(data.error || 'Ошибка');

            currentSyncId = data.syncId;

            // Создаем контейнер для прогресс-бара
            createProgressBar();

            startPolling(currentSyncId);
        } catch (err) {
            alert('Ошибка: ' + err.message);
            resetSyncButton();
        }
    });

    function createProgressBar() {
        previewSection.style.display = 'block';
        const container = document.createElement('div');
        container.id = 'syncProgressContainer';
        container.className = 'alert alert-secondary';
        container.innerHTML = `
            <p class="mb-2">Сканирование облака... <span id="syncProgressText" class="fw-bold">0 / 0</span></p>
            <div class="progress">
                <div id="syncProgressBar" class="progress-bar progress-bar-striped progress-bar-animated" 
                     role="progressbar" style="width: 0%;">0%</div>
            </div>
        `;
        // Вставляем перед списком файлов
        previewSection.insertBefore(container, previewFilesList);
    }

    function startPolling(syncId) {
        if (pollHandle) clearInterval(pollHandle);

        pollHandle = setInterval(async () => {
            try {
                const r = await fetch(`/api/nextcloud/preview/${syncId}`);
                if (!r.ok) {
                    clearInterval(pollHandle);
                    resetSyncButton();
                    return;
                }

                const data = await r.json();

                if (data.isCompleted) {
                    clearInterval(pollHandle);
                    resetSyncButton();

                    // Удаляем прогресс-бар
                    const progressBox = document.getElementById('syncProgressContainer');
                    if (progressBox) progressBox.remove();

                    if (data.error) {
                        alert('Ошибка: ' + data.error);
                        return;
                    }

                    if (data.files.length === 0) {
                        alert(`Нет новых файлов для обработки. Пропущено (уже в БД): ${data.skippedFilesCount}`);
                        return;
                    }

                    renderPreview(data);
                } else {
                    // Обновляем прогресс-бар
                    const textEl = document.getElementById('syncProgressText');
                    const barEl = document.getElementById('syncProgressBar');

                    if (textEl && barEl && data.totalFilesDiscovered > 0) {
                        const percent = Math.round((data.processedFiles / data.totalFilesDiscovered) * 100);
                        barEl.style.width = percent + '%';
                        barEl.textContent = percent + '%';
                        textEl.textContent = `${data.processedFiles} / ${data.totalFilesDiscovered}`;
                    }
                }
            } catch {
                // Игнорируем сетевые ошибки при опросе
            }
        }, 1500); // Опрашиваем чуть чаще (раз в 1.5 сек), чтобы прогресс-бар плавнее двигался
    }

    function resetSyncButton() {
        syncBtn.disabled = false;
        syncBtn.textContent = 'Синхронизировать всё';
    }

    function renderPreview(data) {
        previewSection.style.display = 'block';
        document.getElementById('previewFilesCount').textContent = data.files.length;
        document.getElementById('previewNewCount').textContent = data.totalNewIocs;
        document.getElementById('previewExistingCount').textContent = data.totalExistingIocs;

        const skippedEl = document.getElementById('previewSkippedCount');
        if (skippedEl) skippedEl.textContent = data.skippedFilesCount;

        previewFilesList.innerHTML = '';

        data.files.forEach((file, index) => {
            const collapseId = `collapse_${index}`;
            const item = document.createElement('div');
            item.className = 'accordion-item';

            let iocsHtml = '';
            if (file.newIocs && file.newIocs.length > 0) {
                iocsHtml = `<ul class="list-group list-group-flush mt-2">`;
                file.newIocs.forEach(ioc => {
                    iocsHtml += `<li class="list-group-item small"><strong>${escapeHtml(ioc.type)}:</strong> ${escapeHtml(ioc.value)}</li>`;
                });
                if (file.newCount > 100) {
                    iocsHtml += `<li class="list-group-item small text-muted">... и ещё ${file.newCount - 100} IOC</li>`;
                }
                iocsHtml += `</ul>`;
            } else {
                iocsHtml = `<p class="text-muted small mt-2">Нет новых IOC</p>`;
            }

            item.innerHTML = `
                <h2 class="accordion-header">
                    <button class="accordion-button collapsed" type="button" data-bs-toggle="collapse" data-bs-target="#${collapseId}">
                        <div class="d-flex justify-content-between w-100 me-3">
                            <span class="text-truncate" style="max-width: 70%;">${escapeHtml(file.fileName)}</span>
                            <span>
                                <span class="badge bg-success">Новых: ${file.newCount}</span>
                                <span class="badge bg-warning text-dark">Существующих: ${file.existingCount}</span>
                            </span>
                        </div>
                    </button>
                </h2>
                <div id="${collapseId}" class="accordion-collapse collapse">
                    <div class="accordion-body">
                        <p class="small text-muted mb-1">Дата документа: ${new Date(file.documentDate).toLocaleDateString()} | Провайдер: ${escapeHtml(file.documentProvider)}</p>
                        <h6>Новые IOC (требуют сверки):</h6>
                        ${iocsHtml}
                    </div>
                </div>
            `;
            previewFilesList.appendChild(item);
        });

        // Привязываем обработчики к кнопкам после рендера
        document.getElementById('commitBtn').onclick = () => commit();
        document.getElementById('cancelBtn').onclick = () => cancel();
    }

    async function commit() {
        if (!confirm('Вы уверены, что хотите добавить эти данные в БД?')) return;

        const commitBtn = document.getElementById('commitBtn');
        commitBtn.disabled = true;
        commitBtn.textContent = 'Сохранение...';

        try {
            const r = await fetch(`/api/nextcloud/commit/${currentSyncId}`, { method: 'POST' });
            const data = await r.json();
            if (!r.ok) throw new Error(data.error || 'Ошибка при коммите');

            alert('Данные успешно добавлены в БД!');
            previewSection.style.display = 'none';
            currentSyncId = null;
        } catch (err) {
            alert('Ошибка: ' + err.message);
        } finally {
            commitBtn.disabled = false;
            commitBtn.textContent = 'Подтвердить и добавить в БД';
        }
    }

    async function cancel() {
        if (!confirm('Отменить синхронизацию? Данные не будут добавлены в БД.')) return;

        // Останавливаем поллинг сразу же
        if (pollHandle) clearInterval(pollHandle);
        resetSyncButton();

        try {
            await fetch(`/api/nextcloud/cancel/${currentSyncId}`, { method: 'POST' });
        } catch (e) {
            console.warn('Ошибка при отмене на сервере', e);
        }

        previewSection.style.display = 'none';
        const progressBox = document.getElementById('syncProgressContainer');
        if (progressBox) progressBox.remove();
        currentSyncId = null;
    }

    function escapeHtml(s) {
        if (!s) return '';
        return String(s).replace(/[&<>"']/g, c => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }
});