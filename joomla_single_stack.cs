cloudscript joomla_single_stack
    version                 = '2012-05-20'  
    result_template         = joomla_result_template

globals
    mysql_root_password     = lib::random_password()
    joomla_admin_password   = lib::random_password()
    joomla_slice_user       = 'joomla'
    joomla_db_name          = 'joomla'
    joomla_db_username      = 'joomla'
    joomla_db_password      = lib::random_password()

thread joomla_setup
    tasks                   = [joomla_server_setup]

task joomla_server_setup

    #-------------------------------
    # create joomla server keys
    #-------------------------------
    
    # create joomla server root password key
    /key/password joomla_server_password_key read_or_create
        key_group           = _SERVER
    
    # create joomla server console key
    /key/password joomla_server_console_key read_or_create
        key_group           = _CONSOLE
        
    # create storage slice keys
    /key/token joomla_slice_key read_or_create
        username            = joomla_slice_user

    #-------------------------------
    # create joomla bootstrap 
    # script and recipe
    #-------------------------------
    
    # create slice to store script in cloudstorage
    /storage/slice joomla_slice read_or_create
        keys                = [joomla_slice_key]
    
    # create slice container to store script in cloudstorage
    /storage/container joomla_container => [joomla_slice] read_or_create
        slice               = joomla_slice
    
    # place script data in cloudstorage
    /storage/object joomla_bootstrap_object => [joomla_slice] read_or_create
        container_name      = 'joomla_container'
        file_name           = 'bootstrap_joomla.sh'
        slice               = joomla_slice
        content_data        = joomla_bootstrap_data
        
    # associate the cloudstorage object with the joomla script
    /orchestration/script joomla_bootstrap_script => [joomla_slice, joomla_container, joomla_bootstrap_object] read_or_create
        data_uri            = 'cloudstorage://joomla_slice/joomla_container/bootstrap_joomla.sh'
        script_type         = _SHELL
        encoding            = _STORAGE
    
    # create the recipe and associate the script
    /orchestration/recipe joomla_bootstrap_recipe read_or_create
        scripts             = [joomla_bootstrap_script]

    #-------------------------------
    # create the joomla server
    #-------------------------------
    
    /server/cloud joomla_server read_or_create
        hostname            = 'joomla'
        image               = 'Linux Ubuntu Server 10.04 LTS 64-bit'
        service_type        = 'CS1'
        keys                = [joomla_server_password_key, joomla_server_console_key]
        recipes             = [joomla_bootstrap_recipe]

text_template joomla_bootstrap_data
#!/bin/sh

#-------------------------
# Install packages
#-------------------------

# get latest package list
apt-get update

# install apache2
apt-get install -y apache2

# let the user know joomla is coming
echo "Joomla is installing, please wait..." > /var/www/index.html

# prepare mysql preseed
echo "mysql-server-5.1 mysql-server/root_password password {{ mysql_root_password }}" > mysql.preseed
echo "mysql-server-5.1 mysql-server/root_password_again password {{ mysql_root_password }}" >> mysql.preseed
echo "mysql-server-5.1 mysql-server/start_on_boot boolean true" >> mysql.preseed
cat mysql.preseed | sudo debconf-set-selections
rm mysql.preseed

# install php/mysql packages
apt-get -y install mysql-server
apt-get -y install libapache2-mod-php5
apt-get -y install php5-mysql

# restart apache to use the modules
/etc/init.d/apache2 restart

# get and install zip
apt-get -y install unzip

# download wordpress
wget -q -O /tmp/latest.zip http://joomlacode.org/gf/download/frsrelease/15900/68956/Joomla_1.7.2-Stable-Full_Package.zip

# extract it and fix permissions
cd /var/www
unzip /tmp/latest.zip > /dev/null
chown -R www-data:www-data /var/www

#-------------------------
# Set MySQL Permissions
#-------------------------
echo "Doing perms"

cat <<\EOF>/tmp/create_accounts.mysql
INSERT INTO `jos_users` VALUES (62, 'Administrator', 'admin', 'nobody@amazon.com', MD5('{{ joomla_admin_password }}'), 'Super Administrator', 0, 1, '2012-01-01 00:00:00', '2012-01-01 00:00:00', '', '');
INSERT INTO `jos_user_usergroup_map` (`user_id`, `group_id`) VALUES (62,8);
EOF

cat <<\EOF>/tmp/setup.mysql
CREATE DATABASE {{ joomla_db_name }};
CREATE USER '{{ joomla_db_username }}'@'localhost' IDENTIFIED BY '{{ joomla_db_password }}';
GRANT ALL ON {{ joomla_db_name }}.* TO '{{ joomla_db_username }}'@'localhost';
FLUSH PRIVILEGES;
EOF

# run setup.mysql
echo "Running mysql"
mysql --user=root --password='{{ mysql_root_password }}' < /tmp/setup.mysql


# create joomla sql config
echo "Create joomla sql config"
sed -e 's/#__/jos_/g' < /var/www/installation/sql/mysql/joomla.sql > /var/www/joomla.sql

# run joomla sql config
echo "Load joomla sql config"
mysql {{ joomla_db_name }} --user="{{ joomla_db_username }}" --password="{{ joomla_db_password }}" < /var/www/joomla.sql

# create joomla accounts
echo "Create joomla sql accounts"
mysql {{ joomla_db_name }} --user="{{ joomla_db_username }}" --password="{{ joomla_db_password }}" < /tmp/create_accounts.mysql

#-----------------------------
# Create joomla config file
#-----------------------------

# create joomla config
echo "Create joomla php config"
sed -e "s/\$user = ''/\$user = '{{ joomla_db_username }}'/g" \
    -e "s/\$password = ''/\$password = '{{ joomla_db_password }}'/g" \
    -e "s/\$db = ''/\$db = '{{ joomla_db_name }}'/g" \
    -e "s/\$live_site = 'joomla'/\$live_site = ''/g" \
    < /var/www/installation/configuration.php-dist > /var/www/configuration.php

#-----------------------------
# Cleanup
#-----------------------------

echo "Doing cleanup"

# cleanup setup files
rm -f /tmp/setup.mysql
rm -f /tmp/create_accounts.mysql
rm -f /var/www/joomla.sql
rm -Rf /var/www/installation
rm -rf /var/www/index.html
_eof

text_template joomla_result_template

You can now access your Joomla administration site at the
following URL:

http://{{ joomla_server.ipaddress_public }}/administrator 

You can login with the username 'admin' using the 
password '{{ joomla_admin_password }}'.  You can also 
visit  your public Joomla site at the following URL:

http://{{ joomla_server.ipaddress_public }}

_eof
