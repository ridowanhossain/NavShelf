/**
 * NavShelf - Landing Page Core JavaScript
 * Handles Theme Toggling, Interactive Explorer Mockup, FAQ Accordion, & Dynamic Micro-Interactions
 */

document.addEventListener('DOMContentLoaded', () => {
  initTheme();
  initExplorerMockup();
  initFaqAccordion();
  initStatCounters();
  initSmoothScroll();
});

/* ==========================================================================
   1. Theme Management (Light / Dark Mode with System Preference & Storage)
   ========================================================================== */

function initTheme() {
  const themeToggleBtn = document.getElementById('themeToggleBtn');
  const themeIcon = document.getElementById('themeIcon');
  
  // Default is light mode unless user previously toggled to dark
  const savedTheme = localStorage.getItem('navshelf-theme');
  const currentTheme = savedTheme || 'light';
  
  document.documentElement.setAttribute('data-theme', currentTheme);
  updateThemeIcon(currentTheme);

  if (themeToggleBtn) {
    themeToggleBtn.addEventListener('click', () => {
      const activeTheme = document.documentElement.getAttribute('data-theme');
      const newTheme = activeTheme === 'dark' ? 'light' : 'dark';
      
      document.documentElement.setAttribute('data-theme', newTheme);
      localStorage.setItem('navshelf-theme', newTheme);
      updateThemeIcon(newTheme);
    });
  }

  function updateThemeIcon(theme) {
    if (!themeIcon) return;
    if (theme === 'dark') {
      // Moon / Sun transition
      themeIcon.innerHTML = `
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <circle cx="12" cy="12" r="5"></circle>
          <line x1="12" y1="1" x2="12" y2="3"></line>
          <line x1="12" y1="21" x2="12" y2="23"></line>
          <line x1="4.22" y1="4.22" x2="5.64" y2="5.64"></line>
          <line x1="18.36" y1="18.36" x2="19.78" y2="19.78"></line>
          <line x1="1" y1="12" x2="3" y2="12"></line>
          <line x1="21" y1="12" x2="23" y2="12"></line>
          <line x1="4.22" y1="19.78" x2="5.64" y2="18.36"></line>
          <line x1="18.36" y1="5.64" x2="19.78" y2="4.22"></line>
        </svg>
      `;
    } else {
      themeIcon.innerHTML = `
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"></path>
        </svg>
      `;
    }
  }
}

/* ==========================================================================
   2. Interactive Live Windows 11 Explorer Mockup
   ========================================================================== */

const folderMockData = {
  projects: {
    name: 'Projects',
    path: 'This PC > Local Disk (C:) > Developer > Projects',
    files: [
      { name: 'NavShelf-App', type: 'folder', meta: 'Just now' },
      { name: 'React-Dashboard', type: 'folder', meta: 'Yesterday' },
      { name: 'AI-Agent-Core', type: 'folder', meta: '2 days ago' },
      { name: 'config.json', type: 'code', meta: '12 KB' },
      { name: 'README.md', type: 'document', meta: '4 KB' }
    ]
  },
  github: {
    name: 'GitHub Repos',
    path: 'This PC > Local Disk (C:) > GitHub',
    files: [
      { name: 'NavShelf', type: 'folder', meta: 'Just now' },
      { name: 'awesome-tools', type: 'folder', meta: '3 days ago' },
      { name: 'frontend-kit', type: 'folder', meta: '1 week ago' },
      { name: 'release.zip', type: 'zip', meta: '108 KB' }
    ]
  },
  downloads: {
    name: 'Downloads',
    path: 'This PC > Downloads',
    files: [
      { name: 'NavShelf-Setup.exe', type: 'exe', meta: '108 KB' },
      { name: 'Invoice_August.pdf', type: 'pdf', meta: '240 KB' },
      { name: 'wallpaper-4k.png', type: 'image', meta: '4.2 MB' },
      { name: 'Dataset_2026.csv', type: 'code', meta: '1.1 MB' }
    ]
  },
  work: {
    name: 'Work Documents',
    path: 'This PC > Work',
    files: [
      { name: 'Sprint-Planning', type: 'folder', meta: 'Today' },
      { name: 'Client-Proposal.docx', type: 'document', meta: '1.5 MB' },
      { name: 'Budget-Q3.xlsx', type: 'document', meta: '850 KB' },
      { name: 'Design-System.fig', type: 'image', meta: '12 MB' }
    ]
  },
  music: {
    name: 'Music & Audio',
    path: 'This PC > Music',
    files: [
      { name: 'Chill-Synthwave.mp3', type: 'audio', meta: '8.4 MB' },
      { name: 'Focus-Beats-Vol2', type: 'folder', meta: '3 days ago' },
      { name: 'Podcast_Ep14.mp3', type: 'audio', meta: '45 MB' }
    ]
  }
};

function initExplorerMockup() {
  const addressText = document.getElementById('mockAddressText');
  const filesGrid = document.getElementById('mockFilesGrid');
  const notification = document.getElementById('mockNotification');
  const notificationText = document.getElementById('mockNotificationText');
  const chips = document.querySelectorAll('.navshelf-chip');
  const tabs = document.querySelectorAll('.explorer-tab:not(.explorer-tab-new)');
  const newTabBtn = document.querySelector('.explorer-tab-new');
  const sidebarItems = document.querySelectorAll('.sidebar-item');

  function renderFolder(folderKey) {
    const data = folderMockData[folderKey] || folderMockData['projects'];
    if (addressText) addressText.textContent = data.path;

    if (filesGrid) {
      filesGrid.innerHTML = '';
      data.files.forEach(file => {
        const card = document.createElement('div');
        card.className = 'file-card';
        card.innerHTML = `
          <div class="file-icon">${getFileIconSvg(file.type)}</div>
          <div class="file-name" title="${file.name}">${file.name}</div>
          <div class="file-meta">${file.meta}</div>
        `;
        filesGrid.appendChild(card);
      });
    }
  }

  function showToast(message) {
    if (!notification || !notificationText) return;
    notificationText.textContent = message;
    notification.classList.add('show');
    setTimeout(() => {
      notification.classList.remove('show');
    }, 3200);
  }

  // Chip Clicks (Left & Middle Click Simulation)
  chips.forEach(chip => {
    // Normal Left Click
    chip.addEventListener('click', (e) => {
      e.preventDefault();
      chips.forEach(c => c.classList.remove('active'));
      chip.classList.add('active');
      const folderKey = chip.getAttribute('data-folder');
      renderFolder(folderKey);
      showToast(`Navigated to ${folderMockData[folderKey]?.name || 'Folder'}`);
    });

    // Middle Mouse Click (Auxclick in JS)
    chip.addEventListener('auxclick', (e) => {
      if (e.button === 1) { // Middle click (scroll wheel)
        e.preventDefault();
        const folderKey = chip.getAttribute('data-folder');
        const folderName = folderMockData[folderKey]?.name || 'Folder';
        showToast(`⚡ Middle-Click: Opened "${folderName}" in a New Tab!`);
        
        // Dynamically add a tab
        if (newTabBtn) {
          const newTab = document.createElement('div');
          newTab.className = 'explorer-tab active';
          newTab.innerHTML = `<span>${folderName}</span>`;
          tabs.forEach(t => t.classList.remove('active'));
          newTabBtn.parentNode.insertBefore(newTab, newTabBtn);
          renderFolder(folderKey);
        }
      }
    });
  });

  // Tab Clicks
  tabs.forEach(tab => {
    tab.addEventListener('click', () => {
      tabs.forEach(t => t.classList.remove('active'));
      tab.classList.add('active');
      const tabName = tab.querySelector('span')?.textContent.toLowerCase() || 'projects';
      renderFolder(tabName);
    });
  });

  // Sidebar Clicks
  sidebarItems.forEach(item => {
    item.addEventListener('click', () => {
      sidebarItems.forEach(i => i.classList.remove('active'));
      item.classList.add('active');
      const key = item.getAttribute('data-folder') || 'projects';
      renderFolder(key);
    });
  });

  // Initial Render
  renderFolder('projects');
}

function getFileIconSvg(type) {
  switch (type) {
    case 'folder':
      return `<svg viewBox="0 0 24 24" fill="#f59e0b" width="44" height="44"><path d="M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"/></svg>`;
    case 'code':
      return `<svg viewBox="0 0 24 24" fill="#6366f1" width="44" height="44"><path d="M9.4 16.6L4.8 12l4.6-4.6L8 6l-6 6 6 6 1.4-1.4zm5.2 0l4.6-4.6-4.6-4.6L16 6l6 6-6 6-1.4-1.4z"/></svg>`;
    case 'document':
    case 'pdf':
      return `<svg viewBox="0 0 24 24" fill="#ef4444" width="44" height="44"><path d="M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z"/></svg>`;
    case 'image':
      return `<svg viewBox="0 0 24 24" fill="#10b981" width="44" height="44"><path d="M21 19V5c0-1.1-.9-2-2-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2zM8.5 13.5l2.5 3.01L14.5 12l4.5 6H5l3.5-4.5z"/></svg>`;
    case 'audio':
      return `<svg viewBox="0 0 24 24" fill="#ec4899" width="44" height="44"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z"/></svg>`;
    case 'exe':
      return `<svg viewBox="0 0 24 24" fill="#0066ff" width="44" height="44"><path d="M4 4h16v16H4V4zm2 2v12h12V6H6zm3 3h2v6H9V9zm4 0h2v6h-2V9z"/></svg>`;
    default:
      return `<svg viewBox="0 0 24 24" fill="#94a3b8" width="44" height="44"><path d="M14 2H6c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V8l-6-6z"/></svg>`;
  }
}

/* ==========================================================================
   3. FAQ Accordion
   ========================================================================== */

function initFaqAccordion() {
  const faqItems = document.querySelectorAll('.faq-item');
  
  faqItems.forEach(item => {
    const question = item.querySelector('.faq-question');
    const answer = item.querySelector('.faq-answer');
    
    question.addEventListener('click', () => {
      const isActive = item.classList.contains('active');
      
      // Close other items
      faqItems.forEach(other => {
        if (other !== item) {
          other.classList.remove('active');
          const otherAnswer = other.querySelector('.faq-answer');
          if (otherAnswer) otherAnswer.style.maxHeight = null;
        }
      });
      
      // Toggle current item
      if (isActive) {
        item.classList.remove('active');
        answer.style.maxHeight = null;
      } else {
        item.classList.add('active');
        answer.style.maxHeight = answer.scrollHeight + 'px';
      }
    });
  });
}

/* ==========================================================================
   4. Animated Stat Counters on Scroll
   ========================================================================== */

function initStatCounters() {
  const counters = document.querySelectorAll('.counter-val');
  let started = false;

  const observer = new IntersectionObserver((entries) => {
    entries.forEach(entry => {
      if (entry.isIntersecting && !started) {
        started = true;
        counters.forEach(counter => {
          const target = +counter.getAttribute('data-target');
          const suffix = counter.getAttribute('data-suffix') || '';
          let count = 0;
          const speed = 25;
          const step = Math.ceil(target / 40);

          const updateCount = () => {
            count += step;
            if (count < target) {
              counter.innerText = count + suffix;
              setTimeout(updateCount, speed);
            } else {
              counter.innerText = target + suffix;
            }
          };
          updateCount();
        });
      }
    });
  }, { threshold: 0.5 });

  const metricsSection = document.querySelector('.metrics-section');
  if (metricsSection) observer.observe(metricsSection);
}

/* ==========================================================================
   5. Smooth Scroll & Active Nav Spy
   ========================================================================== */

function initSmoothScroll() {
  const links = document.querySelectorAll('a[href^="#"]');
  
  links.forEach(link => {
    link.addEventListener('click', (e) => {
      const targetId = link.getAttribute('href');
      if (targetId === '#' || !targetId) return;
      const targetEl = document.querySelector(targetId);
      if (targetEl) {
        e.preventDefault();
        targetEl.scrollIntoView({ behavior: 'smooth', block: 'start' });
      }
    });
  });
}
