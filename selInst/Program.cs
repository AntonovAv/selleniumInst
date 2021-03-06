using System;
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;
using System.Collections.Generic;
using java.sql;
using OpenQA.Selenium.Remote;

namespace selInst
{
    class MainClass
    {
        //Лайки: не более одного в течении 12 – 26 секунд(за раз – 1000, перерыв 24 часа);
        //Подписки: не более одной в течении 12 – 26 секунд(за раз – 1000, перерыв 24 часов);
        //Отписка: интервал 8-12 секунд и не более 2500 в сутки от НЕвзаимных и 1000 от взаимныхв;
        //Комментарии: не более 12-15 в час с задержкой 400 сек, любое превышение лимитов может быть воспринято как спам.
        
        public static int MAX_UNSUBSCR_PER_LAUNCH = 300; // (not implemented)
        public static int MAX_LIKES_FOR_A_DAY = 1000; // may be it important (not implemented)

        public static int MAX_DAYS_THAT_I_SUBSCR_ON_USER = 2;

        public static int NUM_OF_MAX_SUBSCRIBE_FOR_A_DAY = 150;
        public static int MAX_USERS_BY_TAG = 6;
        public static int MAX_REPEATS_FOR_ALL_TAGS = 10;
        public static int MAX_LIKES_PER_USER = 4;
        public static int MAX_UNSUBSCRIBES_PER_ONE_TIME = 10;

        public static void Main(string[] args)
        {

            DataBaseManager dbManager = new DataBaseManager();
            dbManager.InitTable();

            //String marionetteDriverLocation = "wires.exe";
            //System.Environment.SetEnvironmentVariable("webdriver.gecko.driver", marionetteDriverLocation);
            //DesiredCapabilities capabilities = DesiredCapabilities.Firefox();
            //capabilities.SetCapability("marionette", true);
            //IWebDriver driver = new FirefoxDriver(capabilities);
            //IWebDriver driver = new FirefoxDriver(new FirefoxOptions());
            IWebDriver driver = new FirefoxDriver();

            driver.Manage().Window.Maximize();
            driver.Manage().Timeouts().ImplicitlyWait(TimeSpan.FromSeconds(10));

            // login
            LoginPage.GoToLoginPage(driver);
            System.Threading.Thread.Sleep(4000);
            String[] userParams = System.IO.File.ReadAllLines("userparam.txt", System.Text.Encoding.UTF8);
            LoginPage.Login(driver,userParams[0], userParams[1]);
            System.Threading.Thread.Sleep(5000);

            // unsubscribe from users
         //   Unsubscribe(driver, dbManager);

            Dictionary<String, ISet<String>> allTags = GetTagsFromFile("tags.txt");
            // go to like and subscribe new users
            for (int i = 0; i < MAX_REPEATS_FOR_ALL_TAGS; i++)
            {
                foreach (String tag in allTags.Keys)
                {
                    ISet<String> badTags = allTags[tag];
                    SearchPage search = new SearchPage(driver, tag, badTags);
                    search.GoToSearchPage();
                    System.Threading.Thread.Sleep(4000);

                    ISet<String> usersIds = search.CollectUserIds(MAX_USERS_BY_TAG);

                    // check if possible to subscribe today
                    bool isPossibleToFollowUsersToday = (dbManager.GetNumOfSubscribesForLastDay() < NUM_OF_MAX_SUBSCRIBE_FOR_A_DAY);

                    foreach (string userId in usersIds)
                    {
                        User user = new User(driver, userId);
                        if (!user.TryToOpenPage()) // next if user deleted
                        {
                            continue;
                        }
                        // if i subscribe this user - go next
                        if (user.CheckIfISubscrThisUser())
                        {
                            continue;
                        }
                        user.LikeNPosts(MAX_LIKES_PER_USER);
                        System.Threading.Thread.Sleep(5000);

                        //  subscribe for a user if possible for current day
                        if (isPossibleToFollowUsersToday)
                        {
                            // try. If succesed - add user to db
                            if (user.TryToSubscribe())
                            {
                                dbManager.AddSubscriberAtNow(userId);
                            }
                        }
                        System.Threading.Thread.Sleep(5000); // wait and go next
                    }

                    //unsubscribe next part of users
                    Unsubscribe(driver, dbManager);
                }
            }
        }

        public static Dictionary<String, ISet<String>> GetTagsFromFile(String fileName)
        {
            Dictionary<String, ISet<String>> tagsDictionary = new Dictionary<String, ISet<String>>();
            String[] allTags  = System.IO.File.ReadAllLines(fileName, System.Text.Encoding.UTF8);

            foreach(String tagLine in allTags)
            {
                // part 0 is tag part 1 is bad combined tags
                String[] parts = tagLine.Split(':');
                String currentTag = parts[0];
                if (currentTag.StartsWith("*"))
                {
                    continue;
                }

                ISet<String> badTagsSet = new HashSet<String>();

                if (parts.Length > 1)
                {
                    String[] badTags = parts[1].Split(',');
                    foreach (String badTag in badTags)
                    {
                        badTagsSet.Add(badTag);
                    }
                }
                
                tagsDictionary.Add(currentTag, badTagsSet);
            }
                   
            return tagsDictionary;
        }

        public static void Unsubscribe(IWebDriver driver, DataBaseManager dbManager)
        {
            // unsubscribe all users that i subscr N days before
            List<String> userIdsThatISubscr = dbManager.GetSubscribersIdsForBeforeNDaysAgo(MAX_DAYS_THAT_I_SUBSCR_ON_USER);

            for(int i = 0; i < MAX_UNSUBSCRIBES_PER_ONE_TIME && i < userIdsThatISubscr.Count; i++)
            {
                String userIdForUnscubscr = userIdsThatISubscr[i];

                User unsubscrUser = new User(driver, userIdForUnscubscr);

                bool needToDeleteUserFromDb = false;

                if (unsubscrUser.TryToOpenPage())
                {
                    if (unsubscrUser.CheckIfISubscrThisUser())
                    {
                        if (unsubscrUser.TryToUnSubscr())
                        {
                            System.Threading.Thread.Sleep(2000);
                            
                            unsubscrUser.RefreshPage(); // reload page to check that unsubscribe is performed

                            System.Threading.Thread.Sleep(3000);

                            if (!unsubscrUser.CheckIfISubscrThisUser())
                            {
                                // if unsubscribe - delete user
                                Console.WriteLine("unsubscribe: " + userIdForUnscubscr);
                                needToDeleteUserFromDb = true;
                            }
                            else
                            {
                                Console.WriteLine("faild to unsubscr form '" + userIdForUnscubscr + "' skip");
                            }
                        }
                    }
                    else
                    {
                        // if unsubscribe - delete user
                        Console.WriteLine("unsubscribed already(delete from db): " + userIdForUnscubscr);
                        needToDeleteUserFromDb = true;
                    }
                }
                else
                {
                    // if user will be deleted 
                    needToDeleteUserFromDb = true;
                }

                if (needToDeleteUserFromDb)
                {
                    dbManager.DeleteSubscriber(userIdForUnscubscr);
                }
                System.Threading.Thread.Sleep(3000); // wait and go next
            }
        }
    }


    public class LoginPage
    {
        public static void GoToLoginPage(IWebDriver driver)
        {
            driver.Navigate().GoToUrl("http://instagram.com/");
            driver.FindElement(By.XPath("/html/body/span/section/main/article/div[2]/div[2]/p/a")).Click();
        }
        public static void Login(IWebDriver driver, string login, string password)
        {
            IWebElement loginForm = driver.FindElement(By.Name("username"));
            loginForm.Clear();
            loginForm.SendKeys(login);

            System.Threading.Thread.Sleep(500);

            IWebElement passwordForm = driver.FindElement(By.Name("password"));
            passwordForm.SendKeys(password);

            IWebElement submitButton = driver.FindElement(By.TagName("button"));
            submitButton.Click();
        }
    }

    public class SearchPage
    {
        private int ARTICLE_LIMIT = 30; // max of articles that will be used for search peoples for current tag
        private int MAX_COLUMNS = 3;
        private int MAX_VIEWD_ROWS_AT_F_TIME = 4;

        private String searchTag;
        private ISet<String> badCombinationTags;

        private IWebDriver driver;

        public SearchPage(IWebDriver driver, String searchTag, ISet<String> badTags)
        {
            this.driver = driver;
            this.searchTag = searchTag;
            this.badCombinationTags = badTags;
        }

        public void GoToSearchPage()
        {
            driver.Navigate().GoToUrl("http://instagram.com/explore/tags/" + searchTag);
        }

        // time approximetly one article per 6 sec ( in bad case max time for a page 6*ARTICLE_LIMIT sec )
        public ISet<String> CollectUserIds(int limit)
        {
            ISet<String> userIds = new HashSet<String>();
            int counterOfOpenedArticles = 0;
            bool hasMoreArticles = true;

            int rowCount = 1; 
            int columnCount = 1;

            while (userIds.Count < limit && counterOfOpenedArticles < ARTICLE_LIMIT && hasMoreArticles )
            {
                System.Threading.Thread.Sleep(2000); // wait
                if (TryToOpenArticle(rowCount, columnCount))
                {
                    counterOfOpenedArticles++;
                    columnCount++;

                    System.Threading.Thread.Sleep(3000); //wait after open

                    Article article = new Article(driver);
                    ISet<String> articleTags = article.GetArticleTags();

                    bool isExistsBadTag = false;
                    foreach (String tag in articleTags)
                    {
                        if (badCombinationTags.Contains(tag.ToLower()))
                        {
                            Console.WriteLine("Founded article with searched tag '" + searchTag + "' bad tag: '" + tag + "' fother tags for article: ");
                            // write tags to console
                            foreach (String badTag in articleTags)
                            {
                                Console.Write("#" + badTag + ",");
                            }
                            Console.WriteLine("\n-----------------------");

                            isExistsBadTag = true; // go to next article if this artcile contains bad tags
                            break; // go away from checking
                        }
                    }

                    if( !isExistsBadTag)
                    {
                        //if there is no bad tags for article - add user 
                        String userId = article.GetUserId();
                        if (userId != null)
                        {
                            userIds.Add(userId);
                        }
                    }
                    article.Close();
                }
                else
                {
                    if (rowCount > MAX_VIEWD_ROWS_AT_F_TIME)
                    {
                        Boolean successedLoad = TryToLoadMoreArticles();
                        // if not loaded bye - break otherwise go to next row
                        hasMoreArticles = successedLoad;
                    }
                    else
                    {
                        //bye bye this page ( no enough articles )
                        hasMoreArticles = false;
                    }
                }

                if (columnCount > MAX_COLUMNS)
                {
                    columnCount = 1;
                    rowCount++;
                }
            }

            return userIds;
        }

        // 3 columns on row.Open popup with article
        private Boolean TryToOpenArticle(int row, int column)
        {
            IWebElement article;
            try
            {
                article = driver.FindElement(By.XPath("/html/body/span/section/main/article/div[2]/div[1]/div[" + row + "]/a[" + column + "]"));
            }
            catch (NoSuchElementException)
            {
                return false;
            }
            article.Click();
            return true;
        }

        private Boolean TryToLoadMoreArticles()
        {
            IWebElement loadMore;
            try
            {
                loadMore = driver.FindElement(By.XPath("/html/body/span/section/main/article/div[2]/div[3]"));
            }
            catch (NoSuchElementException)
            {
                return false;
            }
            loadMore.Click();
            return true;
        }
    }

	public class Article
	{
		private IWebDriver driver;

		public Article(IWebDriver driver)
		{
			this.driver = driver;
		}

		public void Close()
		{
            try
            {
                IWebElement closeButton = driver.FindElement(By.XPath("/html/body/div[2]/div/button"));

                closeButton.Click(); //
            }
            catch (NoSuchElementException)
            {
                // ok.maybe article already closed
            }
		}

        public String GetUserId()
        {
            try
            {
                IWebElement user = driver.FindElement(By.XPath("/html/body/div[2]/div/div[2]/div/article/header/div/a[1]"));
                return user.Text;
            }
            catch (NoSuchElementException)
            {
                return null;
            }
        }

        // get tags for article from comments of article owner
        public ISet<String> GetArticleTags()
        {
            ISet<String> articleTags = new HashSet<String>();

            String userId = GetUserId(); // get owner of article 

            
            if( userId != null)
            {
                try
                {
                    IReadOnlyCollection<IWebElement> visibleComments = driver.FindElements(By.XPath("/html/body/div[2]/div/div[2]/div/article/div[2]/ul/li"));

                    foreach(IWebElement comment in visibleComments)
                    {
                        String commentOwner = comment.FindElement(By.XPath("//h1/a")).Text;

                        // if user of first comment is owner of article get tags from comment 
                        if (userId.Equals(commentOwner))
                        {
                            IReadOnlyCollection<IWebElement> tagsElements = comment.FindElements(By.XPath("//h1/span/a"));

                            foreach (IWebElement tagEl in tagsElements)
                            {
                                // check if element is tag
                                String tag = tagEl.Text;
                                if (tag.StartsWith("#"))
                                {
                                    articleTags.Add(tag.Substring(1)); // delete #
                                }
                            }
                        }
                    }
                }
                catch (NoSuchElementException)
                {
                    Console.WriteLine("No tags for article of user " + userId);
                }
            }
         
            return articleTags;
        }
	}


	public class User
	{
        private String IS_I_NOT_FOLLOW_TEXT = "подписаться";
        private String IS_I_FOLLOW_TEXT = "подписки";
        private String IS_LIKED_BUTTON_TEXT = "не нравится";
        

        private int MAX_VISIBLE_ROWS = 4;
        private int MAX_COLUMNS_IN_ROW = 3;

        private IWebDriver driver;
        private String userId;
        private IWebElement userPage;
           

		public User(IWebDriver driver, String id)
		{
			this.driver = driver;
            this.userId = id;
		}

        public bool TryToOpenPage()
        {
            try
            {
                driver.Navigate().GoToUrl("http://instagram.com/" + userId);
                System.Threading.Thread.Sleep(5000);
                userPage = driver.FindElement(By.XPath("html/body/span/section/main/article"));
            }
            catch (NoSuchElementException)
            {
                return false;
            }
            return true;
        }

        public void RefreshPage()
        {
            driver.Navigate().Refresh();
            userPage = driver.FindElement(By.XPath("html/body/span/section/main/article"));
        }

        private IWebElement GetSubscrButton()
        {
            return userPage.FindElement(By.XPath("//header/div[2]/div[1]/span/button"));
        }

        public bool CheckIfISubscrThisUser()
        {
            bool iSubscr = false;
            try
            {
                String textOfFollowButton = GetSubscrButton().Text;
                iSubscr = IS_I_FOLLOW_TEXT.Equals(textOfFollowButton.Trim().ToLower());
            } catch (NoSuchElementException)
            {
                iSubscr = false;
            }
            return iSubscr;
        }

        public bool TryToSubscribe()
        {
            bool succSubscr = false;
            try
            {
                GetSubscrButton().Click(); ;
                System.Threading.Thread.Sleep(2000);
                succSubscr = IS_I_FOLLOW_TEXT.Equals(GetSubscrButton().Text.Trim().ToLower());
            }
            catch (NoSuchElementException)
            {
                succSubscr = false;
            }
            return succSubscr;
        }

        public bool TryToUnSubscr()
        {
            bool succUnSubscr = false;
            try
            {
                GetSubscrButton().Click(); ;
                System.Threading.Thread.Sleep(2000);
                succUnSubscr = IS_I_NOT_FOLLOW_TEXT.Equals(GetSubscrButton().Text.Trim().ToLower());
            }
            catch (NoSuchElementException)
            {
                succUnSubscr = false;
            }
            return succUnSubscr;
        }

        // returns num of liked posts
        public int LikeNPosts(int nPosts)
        {
            int rowNum = 1;
            bool morePostsAreExists = true;

            List<IWebElement> visiblePosts = new List<IWebElement>();
            // collect all wisible post on page
            while (rowNum < MAX_VISIBLE_ROWS && morePostsAreExists)
            {
                IReadOnlyCollection<IWebElement> postFromRow = userPage.FindElements(By.XPath("//div/div[1]/div[" + rowNum + "]/a"));
                // its means that next row is empty
                if (postFromRow.Count == 0)
                {
                    morePostsAreExists = false;
                }
                visiblePosts.AddRange(postFromRow);
                
                rowNum++;
            }

            int likedPosts = 0;
            bool isExistsLikedPost = false; // if we liked post of this user - leave user 
            // like posts (random)
            Random random = new Random();
            while (likedPosts < nPosts && visiblePosts.Count > 0 && !isExistsLikedPost)
            {
                int index = 0/*random.Next(visiblePosts.Count)*/;

                visiblePosts[index].Click(); // get random post 
                visiblePosts.RemoveAt(index); // delete from collection
                System.Threading.Thread.Sleep(2000);

                try
                {
                    IWebElement like = driver.FindElement(By.XPath("/html/body/div[2]/div/div[2]/div/article/div[2]/section[2]/a"));

                    // if like not  exists than liked
                    if (!like.Text.Trim().ToLower().Equals(IS_LIKED_BUTTON_TEXT))
                    {
                        like.Click();
                        likedPosts++;
                    } else
                    {
                        isExistsLikedPost = true;
                    }
                }
                catch (NoSuchElementException)
                {
                    // go to next post
                }
                System.Threading.Thread.Sleep(2000);
                // close post
                try
                {
                    IWebElement closeButton = driver.FindElement(By.XPath("/html/body/div[2]/div/button"));
                    closeButton.Click();
                }
                catch (NoSuchElementException)
                {
                    // hmm.. it's a bad situation
                }
                
                System.Threading.Thread.Sleep(4000); // before next post
            }
            return likedPosts;
        }
	}
    
    /*
      Class used for working with h2 database
    */
    public class DataBaseManager
    {

        public DataBaseManager()
        {
            org.h2.Driver.load();
        }

        public Connection GetConnection()
        {
            Connection conn = DriverManager.getConnection("jdbc:h2:./instdb", "root", "root");
            conn.setAutoCommit(true);
            return conn;
        }

        public void InitTable()
        {
            Connection conn = GetConnection();
            Statement stat = conn.createStatement();
            stat.executeUpdate("CREATE TABLE IF NOT EXISTS `subscribies` ( `inst_id` VARCHAR(200) PRIMARY KEY NOT NULL, `subscribe_date` DATETIME NOT NULL)");
            stat.close();
            conn.close();
        }

        public List<string> GetSubscribersIdsForBeforeNDaysAgo(int nDays)
        {
            Connection conn = GetConnection();
            PreparedStatement preparedStatement = conn.prepareStatement("SELECT inst_id FROM `subscribies` WHERE `subscribe_date` < ?");

            DateTime currentDateTime = DateTime.Now;
            DateTime dateMinusDay = currentDateTime.AddDays(-nDays);
            preparedStatement.setString(1, dateMinusDay.ToString("o"));
            ResultSet rs = preparedStatement.executeQuery();

            List<string> subscribers = new List<string>();
            while (rs.next())
            {
                subscribers.Add(rs.getString(1));
            }
            preparedStatement.close();
            conn.close();

            return subscribers;
        }

        public void AddSubscriberAtNow(string id)
        {
            Connection conn = GetConnection();

            // if user is exist - delete (instead update) . It's means that we unsubscribe  user by myself
            PreparedStatement preparedStatement = conn.prepareStatement("DELETE FROM `subscribies` WHERE `inst_id` = ?");
            preparedStatement.setString(1, id);
            preparedStatement.executeUpdate();
            preparedStatement.close();
            // insert id to db
            preparedStatement = conn.prepareStatement("INSERT INTO `subscribies` (`inst_id`,`subscribe_date`) VALUES (?,?)");
            preparedStatement.setString(1, id);
            preparedStatement.setString(2, DateTime.Now.ToString("o"));
            preparedStatement.executeUpdate();
            preparedStatement.close();
            conn.close();
        }

        public void DeleteSubscriber(string id)
        {
            Connection conn = GetConnection();
            PreparedStatement preparedStatement = conn.prepareStatement("DELETE FROM `subscribies` WHERE `inst_id`=?");
            preparedStatement.setString(1, id);
            preparedStatement.executeUpdate();
            preparedStatement.close();
            conn.close();
        }

        public int GetNumOfSubscribesForLastDay()
        {
            Connection conn = GetConnection();
            PreparedStatement preparedStatement = conn.prepareStatement("SELECT COUNT( `inst_id` )  FROM `subscribies` WHERE `subscribe_date` BETWEEN ? AND ?");
            DateTime currentDateTime = DateTime.Now;
            DateTime dateMinusDay = currentDateTime.AddDays(-1);
            preparedStatement.setString(1, dateMinusDay.ToString("o"));
            preparedStatement.setString(2, currentDateTime.ToString("o"));

            ResultSet rs = preparedStatement.executeQuery();
            int count = 0;
            while (rs.next())
            {
                count = rs.getInt(1);
            }
            preparedStatement.close();
            conn.close();
            return count;
        }
    }
}
