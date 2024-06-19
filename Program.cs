using System.Net.Mime;
using System.Text;

const string html =
    """
    <!DOCTYPE html>
    <html lang="en-us">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Short Lived Redirect</title>
        </head>
        <body>
            <main>
                <h1>Short Lived Redirect</h1>
                <p>Create short lived (5 minutes) shortened redirect URLs.</p>
                <div>
                    <input id="url" type="url" placeholder="Enter URL to shorten">
                    <button id="shorten">Shorten</button>
                </div>
                <p id="result">&nbsp;</p>
            </main>
            <style>
                * {
                    box-sizing: border-box;
                }
            
                body {
                    background-color: #292c41;
                    color: #ecf0f1;
                    margin: 0;
                    padding: 0;
                }
                
                main {
                    margin: 10% auto 0;
                    padding: 0;
                    width: 300px;
                }
                
                h1 {
                    margin: 0 0 .5rem 0;
                    padding: 0;
                    text-align: center;
                    width: 100%;
                }
                
                p {
                    margin: 0 0 2rem 0;
                    padding: 0;
                    text-align: center;
                    transition: .25s;
                    width: 100%;
                }
                
                p.error {
                    color: #e74c3c;
                }
                
                input {
                    margin: 0 0 .2rem 0;
                    padding: 10px;
                    width: 100%;
                }
                
                button {
                    margin: 0 0 1rem 0;
                    padding: 10px;
                    width: 100%;
                }
                
                a {
                    color: #3498db;
                    text-decoration: none;
                }
            </style>
            <script>
                (() => {
                    const button = document.querySelector('button#shorten');
                    const url = document.querySelector('input#url');
                    const result = document.querySelector('p#result');
                    
                    button.addEventListener('click', async () => {
                        const value = url.value.trim();
                        
                        if (value === '') {
                            return;
                        }
                        
                        const res = await fetch('/', {
                            body: JSON.stringify({
                                url: value
                            }),
                            headers: {
                                'Content-Type': 'application/json'
                            },
                            method: 'POST'
                        });
                        
                        if (res.status === 400) {
                            result.classList.add('error');
                            result.innerText = 'Invalid URL! Must be an absolute URL.';
                        }
                        else if (res.status !== 200) {
                            result.classList.add('error');
                            result.innerText = 'Error! Check console for more details.';
                        }
                        else {
                            const obj = await res.json();
                            
                            result.classList.remove('error');
                            result.innerHTML = `<a href="${obj.url}">${obj.url}</a>`;
                        }
                    });
                })();
            </script>
        </body>
    </html>
    """;

const string characters = "abcdefghijklmnopqrstuvwxyz1234567890-";

bool? isHttps = Environment.GetEnvironmentVariable("SLR_IS_HTTPS") is null ? null : true;
var hostname = Environment.GetEnvironmentVariable("SLR_REDIRECT_HOSTNAME");

var redirects = new Dictionary<string, UrlEntry>();
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
        
var app = builder.Build();

app.UseHsts();
app.UseHttpsRedirection();

app.MapGet("/", ctx =>
{
    ctx.Response.ContentType = MediaTypeNames.Text.Html;
    ctx.Response.ContentLength = Encoding.UTF8.GetByteCount(html);

    return ctx.Response.WriteAsync(html);
});

app.MapGet("/{slug}", (HttpContext ctx, string slug) =>
{
    if (!redirects.TryGetValue(slug.ToLower(), out var entry))
    {
        return Results.NotFound();
    }

    if (DateTimeOffset.Now <= entry.Expires)
    {
        return ctx.Request.Query.ContainsKey("reveal")
            ? Results.Ok(entry)
            : Results.Redirect(entry.Url.ToString());
    }

    redirects.Remove(slug);
    return Results.NotFound();
});

app.MapPost("/", (HttpContext ctx, CreatePayload payload) =>
{
    if (!payload.Url.IsAbsoluteUri)
    {
        return Results.BadRequest(new
        {
            message = "Invalid URL! Must be an absolute URL."
        });
    }

    if (payload.ExpiresIn > 5)
    {
        return Results.BadRequest(new
        {
            message = "Maximum value for expiration is 5 minutes. Defaults to 5 minutes."
        });
    }
    
    string slug;

    while (true)
    {
        slug = new string(Enumerable.Repeat(characters, 8)
            .Select(n => n[Random.Shared.Next(n.Length)]).ToArray());

        if (!redirects.ContainsKey(slug))
        {
            break;
        }
    }

    redirects.Add(
        slug,
        new(payload.Url, DateTimeOffset.Now.AddMinutes(payload.ExpiresIn ?? 5)));
    
    var keys = redirects.Keys.ToArray();

    foreach (var key in keys)
    {
        if (DateTimeOffset.Now > redirects[key].Expires)
        {
            redirects.Remove(key);
        }
    }
    
    var url = $"http{(isHttps ?? ctx.Request.IsHttps ? "s" : "")}://{hostname ?? ctx.Request.Host.ToString()}/{slug}";

    return Results.Ok(new
    {
        url
    });
});

await app.RunAsync();

internal record CreatePayload(Uri Url, uint? ExpiresIn);

internal record UrlEntry(Uri Url, DateTimeOffset Expires);