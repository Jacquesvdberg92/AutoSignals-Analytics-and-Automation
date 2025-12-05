
  //switcher color pickers
  const pickrContainerPrimary = document.querySelector(
    ".pickr-container-primary"
  );
  const themeContainerPrimary = document.querySelector(
    ".theme-container-primary"
  );
  /* for theme primary */
  const nanoThemes = [
    [
      "nano",
      {
        defaultRepresentation: "RGB",
        components: {
          preview: true,
          opacity: false,
          hue: true,

          interaction: {
            hex: false,
            rgba: true,
            hsva: false,
            input: true,
            clear: false,
            save: false,
          },
        },
      },
    ],
  ];
  const nanoButtons = [];
  let nanoPickr = null;
  for (const [theme, config] of nanoThemes) {
    const button = document.createElement("button");
    button.innerHTML = theme;
    nanoButtons.push(button);

    button.addEventListener("click", () => {
      const el = document.createElement("p");
      pickrContainerPrimary.appendChild(el);

      /* Delete previous instance */
      if (nanoPickr) {
        nanoPickr.destroyAndRemove();
      }

      /* Apply active class */
      for (const btn of nanoButtons) {
        btn.classList[btn === button ? "add" : "remove"]("active");
      }

      /* Create fresh instance */
      nanoPickr = new Pickr(
        Object.assign(
          {
            el,
            theme,
            default: "rgb(84, 109, 254)",
          },
          config
        )
      );

      /* Set events */
      nanoPickr.on("changestop", (source, instance) => {
        let color = instance.getColor().toRGBA();
        let html = document.querySelector("html");
        html.style.setProperty(
          "--primary-rgb",
          `${Math.floor(color[0])}, ${Math.floor(color[1])}, ${Math.floor(
            color[2]
          )}`
        );
        /* theme color picker */
        localStorage.setItem(
          "primaryRGB",
          `${Math.floor(color[0])}, ${Math.floor(color[1])}, ${Math.floor(
            color[2]
          )}`
        );
        updateColors();
      });
    });

    themeContainerPrimary.appendChild(button);
  }
  nanoButtons[0].click();
  /* for theme primary */

let mainContent;
(function () {
  let html = document.querySelector("html");
  mainContent = document.querySelector(".main-content");

    localStorageBackup();
    switcherClick();
    checkOptions();
    
})();

function switcherClick() {
  let ltrBtn,
    rtlBtn,
    lightBtn,
    darkBtn,
    primaryDefaultColor1Btn,
    primaryDefaultColor2Btn,
    primaryDefaultColor3Btn,
    primaryDefaultColor4Btn,
    primaryDefaultColor5Btn,
    ResetAll;
  let html = document.querySelector("html");
  lightBtn = document.querySelector("#switcher-light-theme");
  darkBtn = document.querySelector("#switcher-dark-theme");
  ltrBtn = document.querySelector("#switcher-ltr");
  rtlBtn = document.querySelector("#switcher-rtl");
  primaryDefaultColor1Btn = document.querySelector("#switcher-primary");
  primaryDefaultColor2Btn = document.querySelector("#switcher-primary1");
  primaryDefaultColor3Btn = document.querySelector("#switcher-primary2");
  primaryDefaultColor4Btn = document.querySelector("#switcher-primary3");
  primaryDefaultColor5Btn = document.querySelector("#switcher-primary4");
  ResetAll = document.querySelector("#reset-all");

  /* Light Layout Start */
  let lightThemeVar = lightBtn.addEventListener("click", () => {
    lightFn();
    localStorage.setItem("xintraHeader", "light");
    localStorage.setItem("xintraMenu", "light");
  });
  /* Light Layout End */

  /* Dark Layout Start */
  let darkThemeVar = darkBtn.addEventListener("click", () => {
    darkFn();
    localStorage.setItem("xintraMenu", "dark");
    localStorage.setItem("xintraHeader", "dark");
  });
  /* Dark Layout End */

  // primary theme
  let primaryColor1Var = primaryDefaultColor1Btn.addEventListener(
    "click",
    () => {
      localStorage.setItem("primaryRGB", "106, 91, 204");
      html.style.setProperty("--primary-rgb", `106, 91, 204`);
      updateColors();
    }
  );
  if(localStorage.primaryRGB == "106, 91, 204"){
    document.querySelector('#switcher-primary').checked = true;
  }
  let primaryColor2Var = primaryDefaultColor2Btn.addEventListener(
    "click",
    () => {
      localStorage.setItem("primaryRGB", "63, 75, 236");
      html.style.setProperty("--primary-rgb", `63, 75, 236`);
      updateColors();
    }
  );
  if(localStorage.primaryRGB == "63, 75, 236"){
    document.querySelector('#switcher-primary1').checked = true;
  }
  let primaryColor3Var = primaryDefaultColor3Btn.addEventListener(
    "click",
    () => {
      localStorage.setItem("primaryRGB", "0, 123, 167");
      html.style.setProperty("--primary-rgb", `0, 123, 167`);
      updateColors();
    }
  );
  if(localStorage.primaryRGB == "0, 123, 167"){
    document.querySelector('#switcher-primary2').checked = true;
  }
  let primaryColor4Var = primaryDefaultColor4Btn.addEventListener(
    "click",
    () => {
      localStorage.setItem("primaryRGB", "10, 180, 255");
      html.style.setProperty("--primary-rgb", `10, 180, 255`);
      updateColors();
    }
  );
  if(localStorage.primaryRGB == "10, 180, 255"){
    document.querySelector('#switcher-primary3').checked = true;
  }
  let primaryColor5Var = primaryDefaultColor5Btn.addEventListener(
    "click",
    () => {
      localStorage.setItem("primaryRGB", "139, 149, 4");
      html.style.setProperty("--primary-rgb", `139, 149, 4`);
      updateColors();
    }
  );
  if(localStorage.primaryRGB == "139, 149, 4"){
    document.querySelector('#switcher-primary4').checked = true;
  }

  /* rtl start */
  let rtlVar = rtlBtn.addEventListener("click", () => {
    localStorage.setItem("xintrartl", true);
    localStorage.removeItem("xintraltr");
    rtlFn();
  });
  /* rtl end */

  /* ltr start */
  let ltrVar = ltrBtn.addEventListener("click", () => {
    //    local storage
    localStorage.setItem("xintraltr", true);
    localStorage.removeItem("xintrartl");
    ltrFn();
  });
  /* ltr end */

  // reset all start
  let resetVar = ResetAll.addEventListener("click", () => {
    // clear primary & bg color
    html.style.removeProperty(`--primary-rgb`);

    // clear rtl
    html.removeAttribute("dir", "rtl");
    html.setAttribute("dir", "ltr");

    ResetAllFn();
  });
  // reset all start
}

function ltrFn() {
  let html = document.querySelector("html");
  document
    .querySelector("#style")
    ?.setAttribute("href", "../assets/libs/bootstrap/css/bootstrap.min.css");
  html.setAttribute("dir", "ltr");
  document.querySelector("#switcher-ltr").checked = true;
  checkOptions();
}

function rtlFn() {
  let html = document.querySelector("html");
  html.setAttribute("dir", "rtl");
  document
    .querySelector("#style")
    ?.setAttribute(
      "href",
      "../assets/libs/bootstrap/css/bootstrap.rtl.min.css"
    );
  checkOptions();
}

if (localStorage.xintrartl) {
  rtlFn();
}

function lightFn() {
  let html = document.querySelector("html");
  html.setAttribute("data-theme-mode", "light");
  document.querySelector("#switcher-light-theme").checked = true;
  updateColors();
  localStorage.removeItem("xintradarktheme");
  checkOptions();
  // html.style.removeProperty('--primary-rgb');
}

function darkFn() {
  let html = document.querySelector("html");
  html.setAttribute("data-theme-mode", "dark");
  updateColors();
  localStorage.setItem("xintradarktheme", true);
  localStorage.removeItem("xintralighttheme");
  checkOptions();
  // html.style.removeProperty("--primary-rgb");
}

function ResetAllFn() {
  let html = document.querySelector("html");
  checkOptions();

  // clearing localstorage
  localStorage.clear();

  // reseting chart colors
  updateColors();

  // reseting rtl
  ltrFn();

  // reseting dark theme
  lightFn();

  // resetting theme primary
  document.querySelector("#switcher-primary").checked = false
  document.querySelector("#switcher-primary1").checked = false
  document.querySelector("#switcher-primary2").checked = false
  document.querySelector("#switcher-primary3").checked = false
  document.querySelector("#switcher-primary4").checked = false
}

function checkOptions() {
  // dark
  if (localStorage.getItem("xintradarktheme")) {
    document.querySelector("#switcher-dark-theme").checked = true;
  }

  //RTL
  if (localStorage.getItem("xintrartl")) {
    document.querySelector("#switcher-rtl").checked = true;
  }
}

// update Colors
let myVarVal, primaryRGB;
function updateColors() {
  "use strict";
  primaryRGB = getComputedStyle(document.documentElement)
    .getPropertyValue("--primary-rgb")
    .trim();
}
updateColors();

function localStorageBackup() {
  if (localStorage.primaryRGB) {
    if (document.querySelector(".theme-container-primary")) {
      document.querySelector(".theme-container-primary").value =
        localStorage.primaryRGB;
    }
    document
      .querySelector("html")
      .style.setProperty("--primary-rgb", localStorage.primaryRGB);
  }
  if (localStorage.xintradarktheme) {
    let html = document.querySelector("html");
    html.setAttribute("data-theme-mode", "dark");
  }

  if (localStorage.xintrartl) {
    let html = document.querySelector("html");
    html.setAttribute("dir", "rtl");
  }
  if (localStorage.xintralayout) {
    let html = document.querySelector("html");
    let layoutValue = localStorage.getItem("xintralayout");
    html.setAttribute("data-nav-layout", "horizontal");
  }
}