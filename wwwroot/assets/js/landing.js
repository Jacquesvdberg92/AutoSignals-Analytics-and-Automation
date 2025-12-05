(function () {
  "use strict";

  /* tooltip */
  const tooltipTriggerList = document.querySelectorAll(
    '[data-bs-toggle="tooltip"]'
  );
  const tooltipList = [...tooltipTriggerList].map(
    (tooltipTriggerEl) => new bootstrap.Tooltip(tooltipTriggerEl)
  );

  /* popover  */
  const popoverTriggerList = document.querySelectorAll(
    '[data-bs-toggle="popover"]'
  );
  const popoverList = [...popoverTriggerList].map(
    (popoverTriggerEl) => new bootstrap.Popover(popoverTriggerEl)
  );

  /* Choices JS */
  document.addEventListener("DOMContentLoaded", function () {
    var genericExamples = document.querySelectorAll("[data-trigger]");
    for (let i = 0; i < genericExamples.length; ++i) {
      var element = genericExamples[i];
      new Choices(element, {
        allowHTML: true,
        placeholderValue: "This is a placeholder set in the config",
        searchPlaceholderValue: "Search",
      });
    }
  });
  /* Choices JS */

  /* footer year */
  document.getElementById("year").innerHTML = new Date().getFullYear();
  /* footer year */

  // section menu active
  function onScroll(event) {
    const sections = document.querySelectorAll(".side-menu__item");
    const scrollPos =
      window.pageYOffset ||
      document.documentElement.scrollTop ||
      document.body.scrollTop;

    sections.forEach((elem) => {
      const val = elem.getAttribute("href");
      let refElement;
      if (val != "javascript:void(0);" && val !== "#") {
        refElement = document.querySelector(val);
      }
      const scrollTopMinus = scrollPos + 73;
      if (
        refElement?.offsetTop <= scrollTopMinus &&
        refElement?.offsetTop + refElement.offsetHeight > scrollTopMinus
      ) {
        if (elem.parentElement.parentElement.classList.contains("child1")) {
          elem.parentElement.parentElement.parentElement.children[0].classList.add(
            "active"
          );
        }
        elem.classList.add("active");
        if (elem.closest(".child1")?.previousElementSibling) {
          elem.closest(".child1").previousElementSibling.classList.add("active");
        }
      } else {
        elem.classList.remove("active");
      }
    });
  }
  window.document.addEventListener("scroll", onScroll);
  // for menu target scroll on click

  /* back to top */
  const scrollToTop = document.querySelector(".scrollToTop");
  const $rootElement = document.documentElement;
  const $body = document.body;
  window.onscroll = () => {
    const scrollTop = window.scrollY || window.pageYOffset;
    const clientHt = $rootElement.scrollHeight - $rootElement.clientHeight;
    if (window.scrollY > 100) {
      scrollToTop.style.display = "flex";
    } else {
      scrollToTop.style.display = "none";
    }
  };
  scrollToTop.onclick = () => {
    window.scrollTo(0, 0);
  };
  /* back to top */

  /* testimonial swiper service start */
  var swiper = new Swiper(".testimonialSwiperService", {
    slidesPerView: 3,
    spaceBetween: 30,
    loop: true,
    loopFillGroupWithBlank: true,
    pagination: {
        el: ".swiper-pagination",
        dynamicBullets: true,
        clickable: true,
    },
    autoplay: {
        enabled: true,
        delay: 3000,  
        disableOnInteraction: false,
    },
    breakpoints: {
        320: {
            slidesPerView: 1,
            spaceBetween: 10,
        },
        480: {
            slidesPerView: 1,
            spaceBetween: 10,
        },
        1112: {
            slidesPerView: 3,
            spaceBetween: 10,
        },
        1300: {
            slidesPerView: 2,
            spaceBetween: 30,
        }
    },
  });
  /* testimonial swiper service start */

  var checkbox = document.querySelectorAll(".switcher-pricing input[type='checkbox']")[0];
  var yearly1 = document.getElementById("yearly1");
  var monthly1 = document.getElementById("monthly1");

  checkbox.addEventListener("click", function () {
      if (checkbox.checked) {
          yearly1.classList.add("show");
          monthly1.classList.remove("show");
      } else {
          monthly1.classList.add("show");
          yearly1.classList.remove("show");
      }
  });

})();