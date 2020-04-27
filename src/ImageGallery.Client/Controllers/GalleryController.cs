using IdentityModel.Client;
using ImageGallery.Client.Services;
using ImageGallery.Client.ViewModels;
using ImageGallery.Model;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ImageGallery.Client.Controllers
{
    [Authorize]
    public class GalleryController : Controller
    {
        private readonly HttpClient imageGalleryHttpClient;
        private readonly IHttpClientFactory _clientFactory;

        public GalleryController(//IImageGalleryHttpClient imageGalleryHttpClient,
            IHttpClientFactory clientFactory)
        {
            _clientFactory = clientFactory;
            imageGalleryHttpClient = clientFactory.CreateClient("APIClient");
        }

        public async Task<IActionResult> Index()
        {
            await WriteOutIdentityInformation();

            // call the API
            //var httpClient = await _imageGalleryHttpClient.GetClient(); 

            var response = await imageGalleryHttpClient.GetAsync("api/images").ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var imagesAsString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                var galleryIndexViewModel = new GalleryIndexViewModel(
                    JsonConvert.DeserializeObject<IList<Image>>(imagesAsString).ToList());

                return View(galleryIndexViewModel);
            }          

            throw new Exception($"A problem happened while calling the API: {response.ReasonPhrase}");
        }

        public async Task<IActionResult> EditImage(Guid id)
        {
            // call the API
            //var httpClient = await _imageGalleryHttpClient.GetClient();

            var response = await imageGalleryHttpClient.GetAsync($"api/images/{id}").ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var imageAsString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var deserializedImage = JsonConvert.DeserializeObject<Image>(imageAsString);

                var editImageViewModel = new EditImageViewModel()
                {
                    Id = deserializedImage.Id,
                    Title = deserializedImage.Title
                };
                
                return View(editImageViewModel);
            }
           
            throw new Exception($"A problem happened while calling the API: {response.ReasonPhrase}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditImage(EditImageViewModel editImageViewModel)
        {
            if (!ModelState.IsValid)
            {
                return View();
            }

            // create an ImageForUpdate instance
            var imageForUpdate = new ImageForUpdate()
                { Title = editImageViewModel.Title };

            // serialize it
            var serializedImageForUpdate = JsonConvert.SerializeObject(imageForUpdate);

            // call the API
            //var httpClient = await _imageGalleryHttpClient.GetClient();

            var response = await imageGalleryHttpClient.PutAsync(
                $"api/images/{editImageViewModel.Id}",
                new StringContent(serializedImageForUpdate, System.Text.Encoding.Unicode, "application/json"))
                .ConfigureAwait(false);                        

            if (response.IsSuccessStatusCode)
            {
                return RedirectToAction("Index");
            }
          
            throw new Exception($"A problem happened while calling the API: {response.ReasonPhrase}");
        }

        public async Task<IActionResult> DeleteImage(Guid id)
        {
            // call the API
            //var httpClient = await _imageGalleryHttpClient.GetClient();

            var response = await imageGalleryHttpClient.DeleteAsync($"api/images/{id}").ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return RedirectToAction("Index");
            }
       
            throw new Exception($"A problem happened while calling the API: {response.ReasonPhrase}");
        }
        
        [Authorize(Roles ="PayingUser")]
        public IActionResult AddImage()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "PayingUser")]
        public async Task<IActionResult> AddImage(AddImageViewModel addImageViewModel)
        {   
            if (!ModelState.IsValid)
            {
                return View();
            }

            // create an ImageForCreation instance
            var imageForCreation = new ImageForCreation()
                { Title = addImageViewModel.Title };

            // take the first (only) file in the Files list
            var imageFile = addImageViewModel.Files.First();

            if (imageFile.Length > 0)
            {
                using (var fileStream = imageFile.OpenReadStream())
                using (var ms = new MemoryStream())
                {
                    fileStream.CopyTo(ms);
                    imageForCreation.Bytes = ms.ToArray();                     
                }
            }
            
            // serialize it
            var serializedImageForCreation = JsonConvert.SerializeObject(imageForCreation);

            // call the API
            //var httpClient = await _imageGalleryHttpClient.GetClient();

            var response = await imageGalleryHttpClient.PostAsync(
                $"api/images",
                new StringContent(serializedImageForCreation, System.Text.Encoding.Unicode, "application/json"))
                .ConfigureAwait(false); 

            if (response.IsSuccessStatusCode)
            {
                return RedirectToAction("Index");
            }

            throw new Exception($"A problem happened while calling the API: {response.ReasonPhrase}");
        }               

        public async Task Logout()
        {
            //log out of out web app
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            // also out of our Identity provider IDP Spheresoft
            await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
        }

        [Authorize(Policy = "CanOrderFrame")]
        public async Task<IActionResult> OrderFrame()
        {
            var idpClient = _clientFactory.CreateClient("IDPClient");

            //get the userinfo endpoint from idp
            var metaDataResponse = await idpClient.GetDiscoveryDocumentAsync();
            if (metaDataResponse.IsError)
            {
                throw new Exception("Problem accessing the discovery endpoint", metaDataResponse.Exception);
            }

            //get token
            var accessToken = await HttpContext.GetTokenAsync(OpenIdConnectParameterNames.AccessToken);

            //now call the userinfo endpoint on idp with the token
            var userInfoResponse = await idpClient.GetUserInfoAsync(new UserInfoRequest
            {
                Address = metaDataResponse.UserInfoEndpoint,
                Token = accessToken
            });
            if (userInfoResponse.IsError)
            {
                throw new Exception("Problem accessing the UserInfo endpoint", metaDataResponse.Exception);
            }

            var address = userInfoResponse.Claims
                .FirstOrDefault(c => c.Type == "address")?.Value;

            return View(new OrderFrameViewModel(address));
        }

        public async Task WriteOutIdentityInformation()
        {
            //get saved Identity Token
            var identityToken = await HttpContext
                .GetTokenAsync(OpenIdConnectParameterNames.IdToken);

            //write it out
            Debug.WriteLine($"IdentityToken: {identityToken}");

            //write out the user claims
            foreach (var claim in User.Claims)
            {
                Debug.WriteLine($"Claim Type:{claim.Type} - Claim value: {claim.Value}");
            }
        }
    }
}
